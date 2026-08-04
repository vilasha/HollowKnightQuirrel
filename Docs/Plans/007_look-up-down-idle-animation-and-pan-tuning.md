# Implementation Plan — Look Up/Down Idle Animation + Camera Pan Distance Tuning

**Status:** 🟡 DRAFT (round 1, not yet reviewed)
**Author:** implementation-plan-architect
**Date:** 2026-08-04
**Feature:** Two related, independently-shippable pieces of work:

1. **Look up/down character animation** — a cosmetic overlay on the existing
   `Idle` Animator state, gated narrowly to **idle AND grounded**. Holding
   `W` shows a looking-up pose; holding `S` shows a looking-down pose;
   releasing (or doing literally anything else — walk, jump, attack, defend,
   get hurt, die) cancels back immediately. New reference art
   (`Assets/Sprites/Reference/Quirrel_Looking_Up.png`,
   `Quirrel_Looking_Down.png`) needs cropping/resizing into the existing
   per-frame sprite pipeline's conventions before it can be wired in.
2. **Camera pan distance re-tune** — `CameraFollow`'s existing W/S pan
   (`Docs/Plans/006_look-up-down-camera-pan.md`) currently pans by a full
   `orthographicSize` (50% of total screen height). Change it to 40% of
   screen height (`0.8 * orthographicSize`), including the fallback path and
   the existing pan-distance tests.

These two pieces share a *key* (W/S) but are **deliberately decoupled
systems** — `CameraFollow` reads its own W/S state today and must keep doing
so unmodified in shape; the animation gate is a **new, independent** read of
the same physical keys inside `PlayerController`, narrower in scope than the
camera's own gating. See Section 1, Decision 3.

---

## 0. Summary and verified blast radius

**Confirmed from the actual source (read directly, not assumed):**

- `Assets/Scripts/Player/PlayerController.cs` (741 lines) is the single
  authority for all Animator parameter writes and all keyboard input reads
  for the player. `Update()`'s existing call order (verified, lines
  288–341): horizontal input (A/D) → `TryAttack` (J edge) → `DefendHeld`
  (K held) → `TryJump` (Space edge) → `UpdateFacing()` →
  `UpdateAnimatorParameters()`. This plan inserts one new call
  (`UpdateLookState`) into that sequence, after `TryJump` and before
  `UpdateFacing()`/`UpdateAnimatorParameters()` — deliberately after every
  input that can start a full-commit state this same frame, so a same-frame
  Attack/Defend/Walk already reflects in `IsFullyCommitted`/`_horizontalInput`
  by the time the new look-gating logic runs. Jump is the exception:
  `IsGrounded` does not flip same-frame (it's a `FixedUpdate`-computed
  result), so this call-site ordering does not same-frame-cancel a look-pose
  on Jump — that case is instead masked by Animator transition-list priority
  (Decision 4).
- `IsFullyCommitted => (_isAttacking && IsGrounded) || DefendHeld ||
  _isHurtStunned` (line 118) and `IsGrounded` (line 210, real
  `OverlapCircle` result) already exist and are exactly what "not
  jumping/attacking/defending/hurt-stunned" and "grounded" need — no new
  physics or state machine required, only a new *read* of two existing
  members plus the existing private `_horizontalInput` field (for "not
  walking").
- `Hurt()` and `Die()` (lines 559–625) already have an established,
  load-bearing precedent for force-clearing derived-input state at the
  moment an interrupt is *accepted*, independent of `Update()`'s next tick:
  both explicitly set `DefendHeld = false` and `_animator.SetBool
  (DefendHeldHash, false)` even though `DefendHeld` is otherwise a
  continuous per-frame read, purely for "immediate visual consistency" (per
  their own doc comments) against an externally-triggered call arriving
  between two `Update()` frames. This plan's `LookingUp`/`LookingDown` must
  follow the **exact same pattern**, for the exact same reason — see
  Decision 5.
- `Assets/Animations/Quirrel.controller` (read in full, 894 lines): 10
  Animator parameters, 10 states, 19 transitions, 3 Any-State transitions
  (`DieTrigger`→Die, `HurtTrigger`→Hurt, `AttackTrigger`→Attack — fileIDs
  1101013/1101014/1101015). **Any-State transitions apply to every state,
  including any new ones this plan adds, with zero additional wiring** —
  this is exactly what makes "attack/hurt/die cancels a look-pose
  immediately" free, not something this plan needs to build.
- `Idle` (state fileID 1102001)'s **exact** existing outgoing-transition
  order (load-bearing, must not be reordered): `[1]` `IsWalking==true` →
  Walk (fileID 1101001, `TransitionDuration 0.1`, `ExitTime 0.75`,
  `HasExitTime 0`) · `[2]` `JumpTrigger` → JumpAnticipation (1101003,
  duration 0) · `[3]` `DefendHeld==true` → DefendRaise (1101008, duration
  0). This plan **appends** two new transitions after these three
  (`LookingUp==true`→LookUp, `LookingDown==true`→LookDown) — lowest
  priority, and in practice never simultaneously true with `[1]`–`[3]`
  since `LookingUp`/`LookingDown` are code-gated to only ever be `true`
  when none of walking/jumping/defending is happening (Decision 4) — but
  appending last is still the correct, conservative choice.
- `DefendHeld`'s existing Animator-side transition condition is a **single**
  condition (`DefendHeld==true`, fileID 1101008) — no redundant
  `IsGrounded` check on the Animator side, because `DefendHeld` is already
  fully pre-gated in code (`!IsDead && !_isHurtStunned && IsGrounded &&
  Input.GetKey(...)`, line 332). This plan's `LookingUp`/`LookingDown`
  transitions follow the **identical convention**: single-condition,
  because the code-side gate already does all the real work (Decision 4).
- `AnimatorContractTests.cs` (219 lines): `ExpectedParameters` is a
  10-entry array; `Controller_HasExactly10Parameters...` asserts
  `ExpectedParameters.Length == actualParameters.Length` — **this
  assertion self-adjusts to 12 once 2 rows are appended**, it does not need
  a hardcoded `10` touched anywhere. A second existing test
  (`AttackState_HasExactlyThreeOutgoingTransitions...`) is the established
  style/precedent for asserting exact transition order+conditions on a
  state — this plan's QA task mirrors that exact style for `Idle`,
  `LookUp`, and `LookDown`.
- `PlayerControllerTests.cs` (739 lines) already has three precedented
  reflection helpers this plan's tests reuse as-is: `ForceSetDefendHeld`
  (PropertyInfo, sets `DefendHeld`), `ForceSetIsGrounded` (PropertyInfo,
  sets `IsGrounded`), and direct use of `IsDead`'s real public setter (no
  reflection needed). **One new reflection helper is required**: a
  force-setter for the private `_horizontalInput` field (FieldInfo) — see
  Decision 6 for why.
- `CameraFollow.cs` (118 lines), confirmed verbatim: `panDistance =
  _camera != null ? _camera.orthographicSize : _panDistanceFallback;
  targetY = _baseY + panDirection * panDistance;`. `_panDistanceFallback`
  is `[SerializeField] private float _panDistanceFallback = 5f;`, and is
  **explicitly serialized in the scene** — confirmed by direct read of
  `Assets/Scenes/SampleScene.unity` line 224: `_panDistanceFallback: 5`
  on the one `CameraFollow` instance (fileID `519420033`). **This is the
  reason this plan's camera task requires zero scene edits** — see
  Decision 7.
- `CameraFollowTests.cs` (312 lines): four existing tests assert exact
  numeric pan distances against the current 1.0-scale formula —
  `YPosition_PansUpByFallbackDistance...` (asserts `3f + 5f`),
  `YPosition_PansDownByFallbackDistance...` (asserts `3f - 5f`),
  `YPosition_ReturnsToBaseline_AfterPanDirectionReleased` (asserts `3f +
  5f` mid-test, `3f` at the end), `YPosition_UsesLiveCameraOrthographicSize
  ...` (asserts `3f + 7f` with `orthographicSize = 7f`),
  `PanInput_IsNeverBlocked_WhenPlayerControllerUnassigned` (asserts `3f +
  5f`). **All five numeric assertions must change** once the 0.8 scale is
  applied — CLAUDE.md's own instruction on this task named this
  explicitly ("these need updating, not just left to fail").
- Reference art, confirmed via direct image read: `Quirrel_Looking_Up.png`
  is 100×141px (already close to the existing roster's ~100×131 baseline —
  low resize risk). `Quirrel_Looking_Down.png` is 896×1174px — roughly 9×
  oversized in both dimensions versus the target, exactly matching the
  user's own flagged estimate. Both currently render against a flat white
  field (not a checkerboard/transparent preview), consistent with being raw
  opaque AI output needing the same border-connected flood-fill knockout
  technique already built and proven for the original 24-frame sheet
  (`Docs/Plans/002...`, §1.2) — **not a new algorithm, a new caller of the
  existing one.**
- `Assets/Editor/Tools/QuirrelSpriteKnockout.cs` (pure algorithm,
  confirmed read in full): `KnockoutAndFeather(Color32[] pixels, int
  width, int height, byte whiteThreshold = 245)` operates on **any**
  `Color32[]` buffer — it has no dependency on the sheet, on bounding-box
  tables, or on anything sheet-specific. **This is directly reusable
  as-is** for the two new single-image reference files; only a new,
  small caller is needed (Task 1.1), not a modified algorithm.
  `Assets/Editor/Tools/QuirrelSpriteSlicer.cs` is sheet-specific (crops
  fixed, pre-verified bounding boxes out of one known sheet) and is **not**
  reusable as-is for two arbitrary standalone reference images of unknown,
  wildly different native resolutions — a new, small, single-purpose tool
  is warranted (Decision 1), not a rewrite of the slicer.
- `ART.md` §7's Animation Timing table (7 verbs: Idle/Walk/Jump/Attack/
  Defend/Hurt/Die) has no row for this feature — it predates it. A one-row
  addition keeps the bible in sync (folded into Task 6.4 rather than a
  standalone task — this is a five-minute doc edit, not worth its own
  task per this project's delegate-when-cheaper guidance, but still
  tracked as an explicit acceptance criterion so it isn't silently
  skipped).
- `Docs/Backlog.md`: currently empty of entries (confirmed by direct read)
  — no backlog cleanup needed for this plan.
- `Assets/Scenes/SampleScene.unity`: **zero edits required by this entire
  plan.** The Animator changes live inside the already-scene-referenced
  `Quirrel.controller` asset (same asset, no new reference needed); the
  camera pan change is a pure formula change at the read-site plus a
  reused, unchanged serialized field value (Decision 7). This is
  deliberately called out because plan 006 needed one scene edit (Task
  1.3) and plan 002 needed a whole scene-assembly task (3.8) — this plan
  needs neither.

**Complete write list for this plan:**

| Path | Written by | Why |
|---|---|---|
| `Assets/Editor/Tools/QuirrelReferenceSpriteImporter.cs` | Task 1.1 | New tool: crop-to-content, uniform resize, knockout+feather for a single arbitrary reference image |
| `Assets/Sprites/Quirrel/LookUp/Quirrel_LookUp_01.png` (+ `.meta`) | Task 1.2, Task 1.3 | New build-ready sprite frame |
| `Assets/Sprites/Quirrel/LookDown/Quirrel_LookDown_01.png` (+ `.meta`) | Task 1.2, Task 1.3 | New build-ready sprite frame |
| `Assets/Animations/Clips/Quirrel_LookUp.anim` (new) | Task 2.1 | New AnimationClip |
| `Assets/Animations/Clips/Quirrel_LookDown.anim` (new) | Task 2.1 | New AnimationClip |
| `Assets/Animations/Quirrel.controller` | Task 3.1, Task 3.2 | +2 parameters, +2 states, +10 transitions (2 on Idle, 4 each on the new states), then Motion assignment |
| `Assets/Scripts/Player/PlayerController.cs` | Task 4.1 | +2 properties, +2 Animator hash constants, new `UpdateLookState` method + call site, `Hurt()`/`Die()` force-clears |
| `Assets/Scripts/Camera/CameraFollow.cs` | Task 5.1 | Pan-distance formula scaled by 0.8 |
| `Assets/Scripts/Player/Tests/EditMode/PlayerControllerTests.cs` | Task 6.1 | New tests + one new reflection helper |
| `Assets/Scripts/Player/Tests/EditMode/AnimatorContractTests.cs` | Task 6.2 | +2 parameter rows, +3 new transition-order tests |
| `Assets/Scripts/Camera/Tests/EditMode/CameraFollowTests.cs` | Task 6.3 | 5 existing numeric assertions updated to the 0.8 scale |
| `Docs/Plans/002_manual-playtest-protocol.md` | Task 6.4 | New section, mirroring the `5a`/`5b` precedent |
| `ART.md` §7 | Task 6.4 | +1 row in the Animation Timing table |

**Confirmed out of scope:** no save/ScriptableObject schema; no
tags/layers/physics-matrix change; no Input Actions (still legacy
`Input`); no scene edits of any kind (Decision 7); no "look while walking"
or "look while airborne" variants (explicitly excluded by the brief); no
direct `LookUp`↔`LookDown` Animator transition (Decision 8); no change to
`CameraFollow`'s gating (`IsDead || IsFullyCommitted`) — only its pan
*distance*; no sequence-break/ability-gating risk (assessed below).

**Metroidvania-specific check:** the animation half is a pure cosmetic
overlay on an already-reachable state (`Idle`) — it moves no `Transform`,
touches no `Collider2D`/`Rigidbody2D`, grants no new reachable area, and
persists no new unlock. The camera half only changes how far the viewport
pans, never the player's actual position/capabilities (same dismissal
`Docs/Plans/006` already made for the pan feature generally, extended here
to cover the distance re-tune specifically — a smaller pan distance can
only ever reduce, never increase, how much of the level becomes visible
early). Both pieces are outside the ability-gating/progression regression
category.

---

## 1. Design decisions

**1. New, single-purpose Editor tool instead of extending the sheet
slicer.** `QuirrelSpriteSlicer.cs` is built around one known sheet and a
table of pre-verified pixel-exact bounding boxes — it has no bounding-box
*detection* logic at all (Task 1.3 of plan 002 hand-verified every box).
The two new reference images are arbitrary standalone files at wildly
different native resolutions with no pre-verified box. Rather than bolt
detection logic onto the sheet-specific tool, this plan adds a new file,
`QuirrelReferenceSpriteImporter.cs`, that: (a) detects the tight
content bounding box itself (same near-white pixel classification the
knockout algorithm already uses, applied as a bbox scan first), (b) crops
to it, (c) uniformly rescales so the cropped content's height lands on a
caller-supplied target, (d) runs the **existing, unmodified**
`QuirrelSpriteKnockout.KnockoutAndFeather` on the result, (e) writes the
PNG. Reuses the proven algorithm; adds only the new, genuinely-needed
glue. Rejected alternative: hand-cropping in an external image editor
outside the pipeline — rejected because it would leave no repeatable,
reviewable process the way the existing tool does, and this project's own
Task 1.2/1.3 precedent (algorithm vs. accept/reject as separate
disciplines) is worth preserving here too.

**2. Resize-before-knockout, not knockout-before-resize.** The uniform
rescale must happen on the **original opaque** pixel buffer, before any
alpha is introduced. Doing it in the other order (knockout+feather at full
native resolution, then downscale ~9× for the Down reference) would
resample the already-computed 1px feather ring across many source pixels,
diluting or erasing it entirely at the ~9× downscale — defeating the
feather's whole purpose (avoiding a bilinear white-fringe artifact). This
ordering is stated explicitly because it is exactly the kind of ordering
bug that would compile, run, and only show up as a subtle visual fringe
during Task 1.2's accept/reject pass — cheaper to name now than to
discover then.

**3. `UpdateLookState` reads its own independent W/S state — not shared
with `CameraFollow`.** `CameraFollow.Update()` already reads
`Input.GetKey(KeyCode.W/S)` into its own private `_panDirection` field,
completely unaware of `PlayerController`. This plan adds a **second,
independent** read of the same physical keys inside
`PlayerController.Update()`, feeding a local `+1/-1`-accumulation (the same
shape `CameraFollow` and `PlayerController`'s own A/D already use — reused
for *consistency*, not because any code or state is shared) into
`LookingUp`/`LookingDown`. Rejected alternative: have `PlayerController`
expose the raw W/S state and have `CameraFollow` (or vice versa) consume
it — rejected because the brief is explicit that these are two independent
systems with different scopes (camera pans in far more situations than the
animation shows), and sharing a field would create exactly the kind of
accidental coupling a future change to one could silently break the other
through. The one-line cost is two nearly-identical five-line `Input.GetKey`
blocks in two files; the benefit is that a reviewer of either file never
needs to open the other to reason about correctness.

**4. Call-site placement: after every other input handler, before
`UpdateAnimatorParameters()`.** `UpdateLookState` is called from `Update()`
immediately after the existing `TryJump` block and immediately before
`UpdateFacing()`/`UpdateAnimatorParameters()`. This guarantees that if J
(Attack) or K (Defend) also fires *this same frame*, `IsFullyCommitted`
already reflects that by the time `UpdateLookState` computes
`isIdleAndGrounded` — a same-frame cancel, zero added lag. The same holds
for Walk: `_horizontalInput` is set at the very top of `Update()`, before
this call site runs, so a same-frame walk-start also cancels cleanly
through the code-side gate.

**Jump is different, and the code-side gate does not same-frame-cancel
it.** `TryJump()` never touches `IsGrounded` — `IsGrounded` is a
`FixedUpdate`-computed `OverlapCircle` result (`PlayerController.cs`,
confirmed lines 210, 347–355) that stays `true` for the entire frame a jump
starts and for the whole subsequent 0.08s anticipation window, regardless
of where in `Update()` this call site sits. So on the frame a grounded,
idle player holding W presses Space, `isIdleAndGrounded` still evaluates
`true` and `LookingUp` can be (or remain) code-side `true` for that frame
and the following ~0.08s — call-site ordering does **not** cancel the Jump
case the way it does Attack/Defend/Walk. What actually prevents a visible
look-pose during a jump is a separate, Animator-side mechanism: `Idle`'s
outgoing-transition list has `JumpTrigger` at priority `[2]`, ahead of the
newly-appended `LookingUp==true`/`LookingDown==true` at priority `[4]`/`[5]`
— Mecanim evaluates a state's outgoing transitions in list order and takes
the first satisfied one, so `JumpTrigger` wins regardless of `LookingUp`'s
stale-true C# value that same frame. This is the identical
transition-list-priority masking Decision 10 documents for the 0.08s
anticipation window itself: Decision 10 is not a separate edge case, it is
the accepted consequence of this same priority ordering, extended across
the anticipation window's duration. The one case this call-site placement
genuinely does same-frame-cancel end-to-end (code-side, not just masked by
transition priority) is an **externally-triggered** `Hurt()`/`Die()` call
arriving between two `Update()` ticks (e.g., from a future combat system,
or from a manual/MCP test invocation) — see Decision 5 for that case
specifically.

**5. `Hurt()`/`Die()` explicitly force-clear `LookingUp`/`LookingDown`, both
the C# properties and their Animator bools, at the moment the interrupt is
accepted.** This mirrors the *exact* existing precedent for `DefendHeld`
(both methods already force `DefendHeld = false` and
`_animator.SetBool(DefendHeldHash, false)`, even though `DefendHeld` is
otherwise a continuous per-frame read) — for the identical reason: an
externally-triggered call can land between two `Update()` frames, and
without an explicit clear the look-pose could visibly persist for up to
one extra frame after death/hurt starts. This is a small, cheap,
already-precedented defensive addition, not new architecture.

**6. Gating logic lives in a new public, parameterized `UpdateLookState
(bool wHeld, bool sHeld)` method — not inlined directly in `Update()`.**
This follows the *majority* precedent in this file (`TryJump`, `TryAttack`,
`ApplyHorizontalMovement`, `AdvanceJumpTimer`, `AdvanceHurtStunTimer` are
all public and take their "raw" input as parameters specifically so
EditMode tests can drive them without Play Mode) rather than the one
outlier (`DefendHeld`, computed inline in `Update()` and therefore
untestable except via reflection-set final values). The only piece of
`UpdateLookState`'s own gating this project's EditMode constraints cannot
reach is the *raw key read itself* (`Input.GetKey`, called only from
`Update()`) — structurally identical to every other input read in this
codebase (`Docs/Plans/006`, Section 0). One new reflection helper
(`ForceSetHorizontalInput`, on the private `_horizontalInput` field) is
needed for the QA task to exercise the "not walking" branch of the gate,
since `_horizontalInput` is otherwise only ever written inside the
untestable `Update()` — same rationale/convention as this file's existing
`ForceSetDefendHeld`/`ForceSetIsGrounded` helpers.

**7. Camera pan-distance scale is a single multiplier applied uniformly to
both the live-orthographic-size path and the fallback path, at the
read-site — not a change to `_panDistanceFallback`'s stored default.**
`panDistance = (_camera != null ? _camera.orthographicSize :
_panDistanceFallback) * PanDistanceScale`, with `PanDistanceScale = 0.8f`
(a `private const float`, documented inline as "40% of full screen height
= 0.8 × orthographicSize, since orthographicSize is half the vertical
viewport"). `_panDistanceFallback` keeps its existing meaning ("assumed
orthographic size when no live `Camera` is present") and its existing
default (`5f`) completely unchanged — the multiplier converts it (and any
live orthographic size) into the correct 40%-of-screen value automatically.
**Because the field's default/serialized value is untouched, the one
scene instance's already-serialized `_panDistanceFallback: 5` value in
`SampleScene.unity` needs no edit at all** — the new behavior falls out
of the formula change alone. Rejected alternative (the brief's other
named option): changing `_panDistanceFallback`'s default to `4f` directly
— rejected because it would make the field's *name* and *meaning*
("orthographic-size-equivalent fallback") silently diverge from what it
actually represents once read through a 0.8 multiplier elsewhere, and it
would require touching the scene's serialized value as a second edit for
no behavioral benefit over the single-multiplier approach.

**8. No direct `LookUp`↔`LookDown` Animator transition.** If a player
releases W and presses S in the same or a nearby frame, `LookingUp`
becomes `false` and `LookingDown` becomes `true` in the same
`UpdateLookState` call, but `LookUp`'s own transition list only has a path
back to `Idle` (on `LookingUp==false`) and no path directly to `LookDown`.
The practical result: one frame in `Idle` before `LookingDown==true` fires
Idle's own transition into `LookDown` on the *next* frame. This is a
deliberate, named simplification given the brief's explicit "ONLY scope for
the animation" instruction (no request for a seamless up↔down swap) —
flagged here as an accepted minor artifact, not a silently-dropped
requirement. At 1 frame (≤~0.02s at 50fps), this is very unlikely to be
perceptible, and is the kind of item the manual playtest pass (Task 6.4)
explicitly checks for and can escalate if it reads as a visible glitch.

**9. No redundant `IsGrounded` condition on the Animator side for
`Idle`→`LookUp`/`LookDown`.** Matches the established `DefendHeld`
convention exactly (Section 0) — `LookingUp`/`LookingDown` are already
fully pre-gated in code (`IsGrounded && !IsDead && !IsFullyCommitted &&
_horizontalInput == 0f`), so a second `IsGrounded` check on the transition
itself would be pure redundancy, not defense-in-depth (unlike, say, the
Attack no-stack guard, which *is* genuine defense-in-depth because the two
checks are independently reachable).

**10. Considered-and-dismissed edge case: the 0.08s jump-anticipation
window (and the jump-start frame itself).** From the frame Space is
pressed through the whole subsequent 0.08s anticipation window,
`IsGrounded` stays `true` (the character hasn't left the ground yet — see
Decision 4 for why this is *not* cancelled same-frame by the code-side
gate), so if W/S is still held, `LookingUp`/`LookingDown` can remain
logically `true` code-side for that entire span. This has **zero visible
effect**, for the exact reason named in Decision 4: the Animator leaves
`Idle`/`LookUp`/`LookDown` the instant `JumpTrigger` fires, because
`JumpTrigger` sits ahead of `LookingUp==true`/`LookingDown==true` in each
state's outgoing-transition list, and neither `JumpAnticipation` nor any
other non-`Idle`-family state has any transition that reads `LookingUp`/
`LookingDown` at all. This is the same transition-list-priority masking
described in Decision 4, not an independent case — named explicitly here,
and cross-referenced there, so a future reader doesn't mistake either
write-up for describing a separate oversight.

---

## Phase 1 — Reference art → build-ready sprites

#### Task 1.1: [GAMEPLAY] Build the reference-sprite crop/resize/knockout Editor tool
**Depends on:** none
**Parallel:** yes — with everything in Phases 2–5
**Touches:** none (new system) — creates `Assets/Editor/Tools/QuirrelReferenceSpriteImporter.cs`
**Regression risk:** none (new system, lives under `Assets/Editor/`, excluded from player builds). Reuses `QuirrelSpriteKnockout.KnockoutAndFeather` **unmodified** — must not edit that file or its existing self-test.

Implementation, in words: a static method taking (source absolute path,
output absolute path, target content height in px, optional white
threshold defaulting to `QuirrelSpriteKnockout.DefaultWhiteThreshold`).
Loads the source PNG as raw bytes into an in-memory `Texture2D`
(`LoadImage`, same "never touch the source asset's import settings"
discipline `QuirrelSpriteSlicer` already uses). Scans the raw pixel buffer
for the tightest bounding box containing any pixel that is *not*
near-white (same `R,G,B >= 245` classification `QuirrelSpriteKnockout`
already uses internally — read-only reuse of the same threshold constant,
not a copy-pasted magic number). Crops to that box. Computes a uniform
scale factor from the crop's height to the target height and resamples
(bilinear) to the new width/height — **before** any alpha/knockout work,
per Decision 2. Runs `QuirrelSpriteKnockout.KnockoutAndFeather` on the
resized RGBA buffer. Encodes and writes the PNG. Expose via a
`[MenuItem("Tools/Quirrel/Crop, Resize And Knockout Reference Sprite...")]`
that prompts for input file, output folder, and target height, matching
the existing `SliceAndKnockoutMenuCommand` UX convention.

**Acceptance criteria:**
- [ ] Tool correctly detects the tight content bbox on a synthetic test
      texture (reuse/extend the existing `QuirrelSpriteKnockoutSelfTest`
      pattern: a small texture with a solid-color island on a white field)
      — bbox excludes the white border, includes the full island
- [ ] Resize happens on the pre-knockout (fully opaque) buffer, confirmed
      by a test/inspection that the feather ring's alpha ramp is still
      present (not diluted to near-zero width) at the target resolution
      after a large (~9×) downscale
- [ ] Running the tool against `Quirrel_Looking_Down.png` (896×1174) with a
      target height of ~131 produces an output whose content height is
      within a few px of 131, preserving aspect ratio
- [ ] Running the tool against `Quirrel_Looking_Up.png` (100×141, already
      close to target) produces a sane, non-degenerate crop (this is
      close to a no-op resize — confirms the tool doesn't misbehave on
      an input that needs little correction)
- [ ] Tool writes to an arbitrary output folder (scratchpad first, per this
      project's off-tree-iteration convention), never directly into
      `Assets/` on the first pass
- [ ] `QuirrelSpriteKnockout.cs` and its existing self-test are byte-for-byte
      unmodified — **regression check**

---

#### Task 1.2: [ART] Run the tool against both reference images; accept/reject; promote
**Depends on:** Task 1.1
**Parallel:** no
**Touches:** `Assets/Sprites/Reference/Quirrel_Looking_Up.png`,
`Assets/Sprites/Reference/Quirrel_Looking_Down.png` (read-only sources, not
modified); creates `Assets/Sprites/Quirrel/LookUp/**`,
`Assets/Sprites/Quirrel/LookDown/**` (new)
**Regression risk:** none (new system) — future regression surface: any
later re-generation of these two reference images must go back through
this same tool/process to stay pipeline-consistent, same caveat plan 002
carries for the original sheet.

Run Task 1.1's tool against both reference files, output to the scratchpad
first (off-tree iteration, matching plan 002's §1.3 discipline). Target
content height: **~131px**, matching the existing roster's established
baseline — natural per-pose variation is expected and acceptable (the
existing `Jump` 3-pose set already varies 122–142px), so the goal is a
scale that reads consistently next to `Idle` in Unity's Scene/Sprite Editor
view, not a pixel-exact 131.

**Acceptance criteria:**
- [ ] Both outputs visually inspected at ≥400% zoom against a contrasting
      checkerboard background: no white halo/fringe at edges, no
      unintended holes inside the character silhouette (same discipline as
      plan 002 Task 1.3)
- [ ] Both outputs' color values spot-checked against `ART.md` §2.2's
      character base-tone hex table (mask `#F2EEE3`, ink line `#1B1B1F`,
      cloak `#2E323D`) and §2.3's pin-head table (Verdant `#6FA25C`) — any
      significant deviation (e.g., pure-black `#000000` linework instead of
      the house near-black, or a gold/amber pin head reappearing per the
      already-fixed §9 known-gap) is corrected or flagged before
      promotion, not silently accepted
- [ ] Both outputs read at a consistent apparent scale next to the
      existing `Idle` frames when viewed side-by-side in the Unity Editor
      (Sprite Editor or Scene view) — no jarring size mismatch
- [ ] Only the 2 final accepted PNGs are promoted, to
      `Assets/Sprites/Quirrel/LookUp/Quirrel_LookUp_01.png` and
      `Assets/Sprites/Quirrel/LookDown/Quirrel_LookDown_01.png` — no
      throwaway PNGs committed to `Assets/` (LFS churn discipline, matching
      plan 002)
- [ ] Any frame that fails visual/palette QA is round-tripped back to Task
      1.1 for a tool fix (or, if the fault is in the source reference art
      itself, flagged back to the user per this project's own "the AI
      ignored requirements" precedent) — not silently patched by hand
      outside the tool
- [ ] The two source reference files
      (`Assets/Sprites/Reference/Quirrel_Looking_Up.png`/`_Down.png`) are
      left completely unmodified — **regression check**

---

#### Task 1.3: [ART] Unity sprite import configuration for the 2 new frames
**Depends on:** Task 1.2
**Parallel:** no
**Touches:** none (new system) — new `.meta` files only

Mirrors plan 002 Task 1.4's configuration exactly, matching every existing
frame's `.meta` (verified directly against
`Assets/Sprites/Quirrel/Idle/Quirrel_Idle_01.png.meta`): Sprite Mode
Single, Pixels Per Unit 100, Pivot **Bottom** (0.5, 0), Max Size 256,
Filter Mode Bilinear, sRGB on, Alpha Is Transparency on.

**Acceptance criteria:**
- [ ] Both new sprites: Sprite Mode Single, PPU 100, Pivot Bottom (0.5, 0),
      Max Size 256, Filter Bilinear, sRGB on, Alpha Is Transparency on —
      matching the existing `Idle`/`Walk`/etc. `.meta` files field-for-field
- [ ] Visual spot-check in Unity's Sprite Editor: the character's
      feet/lowest visible pixel sits at the sprite's local y=0 for both new
      frames (same check as plan 002 Task 1.4)
- [ ] Both sprites live in their own per-state folders
      (`Assets/Sprites/Quirrel/LookUp/`, `Assets/Sprites/Quirrel/LookDown/`)
      matching the existing per-state folder convention exactly

---

**Phase 1 dependency graph:**
```
1.1 [GAMEPLAY] build tool ──→ 1.2 [ART] run + accept/promote ──→ 1.3 [ART] import config
```

---

## Phase 2 — Animation Clips [ART]

#### Task 2.1: [ART] `Quirrel_LookUp` and `Quirrel_LookDown` AnimationClips
**Depends on:** Task 1.3
**Parallel:** yes — with Phases 3 (topology half only), 4, 5
**Touches:** none (new system)

Both clips are single-frame, duplicate-keyframed, Loop Time **on** —
identical technique to `Quirrel_DefendHold`/`Quirrel_Hurt` (both are
"held while a condition is true" poses, the same shape as these two).

**Acceptance criteria:**
- [ ] `Quirrel_LookUp`: `Quirrel_LookUp_01` only, duplicate-keyframed to a
      short non-zero length (e.g. 0.1s, matching the `DefendHold`/`Hurt`
      convention), Loop Time on
- [ ] `Quirrel_LookDown`: `Quirrel_LookDown_01` only, same technique
- [ ] Both clip names exactly match what Task 3.2 will assign by name
      (`Quirrel_LookUp`, `Quirrel_LookDown`) — no typo/casing drift
- [ ] Both clips play back cleanly in the Animation window preview (no
      missing-sprite warnings)

---

## Phase 3 — Animator Controller [GAMEPLAY]

#### Task 3.1: [GAMEPLAY] Add `LookingUp`/`LookingDown` parameters, `LookUp`/`LookDown` states, and the full transition topology
**Depends on:** none — states/parameters/transitions don't require
AnimationClip assets to exist, only the final Motion assignment does (same
reasoning plan 002's Task 3.5 used)
**Parallel:** yes — with Phases 1, 2, 4, 5 (coordinates only via this
plan's fixed parameter-name contract, no shared files)
**Touches:** `Assets/Animations/Quirrel.controller` (adds 2 parameters, 2
states, 10 new transitions; **does not modify** any existing state's
Motion field, any existing parameter, or any of the 3 existing Any-State
transitions)
**Regression risk:** shared, already-load-bearing asset. The one place
this task can genuinely regress existing behavior: `Idle`'s **existing**
3 outgoing transitions (`IsWalking`→Walk, `JumpTrigger`→JumpAnticipation,
`DefendHeld`→DefendRaise) must keep their exact current order, conditions,
and timing — the 2 new transitions are **appended after** them, never
inserted before or between.

Add 2 Bool parameters: `LookingUp`, `LookingDown` (default `false`,
matching every other Bool parameter's default). Add 2 states: `LookUp`,
`LookDown` (Motion field left empty for now — Task 3.2's job).

`Idle`'s transition list becomes (existing `[1]`–`[3]` byte-identical to
today, `[4]`/`[5]` new, appended last):
`[1]` `IsWalking==true`→Walk (unchanged) · `[2]` `JumpTrigger`→
JumpAnticipation (unchanged) · `[3]` `DefendHeld==true`→DefendRaise
(unchanged) · `[4]` `LookingUp==true`→LookUp (**new**, single condition,
duration 0, no exit time — matching the `DefendHeld` transition's timing
profile, since this is also an instant cosmetic swap, not a locomotion
blend) · `[5]` `LookingDown==true`→LookDown (**new**, same shape,
`LookingDown` condition).

`LookUp`'s transition list (4 total, in order — mirrors `Idle`'s first 3
transitions exactly, by destination/condition/timing, then adds the
release edge last):
`[1]` `IsWalking==true`→Walk (same destination/condition/timing as
`Idle`'s `[1]`) · `[2]` `JumpTrigger`→JumpAnticipation (same as `Idle`'s
`[2]`) · `[3]` `DefendHeld==true`→DefendRaise (same as `Idle`'s `[3]`) ·
`[4]` `LookingUp==false`→Idle (**new**, single condition, duration 0, no
exit time).

`LookDown`'s transition list (4 total, symmetric): `[1]`
`IsWalking==true`→Walk · `[2]` `JumpTrigger`→JumpAnticipation · `[3]`
`DefendHeld==true`→DefendRaise · `[4]` `LookingDown==false`→Idle.

No changes to `Walk`, `JumpAnticipation`, `JumpRise`, `JumpFall`, `Attack`,
`DefendRaise`, `DefendHold`, `Hurt`, or `Die` — and no new transition
*into* `LookUp`/`LookDown` from any state other than `Idle` (explicitly:
`Walk` does **not** gain a path to either look state — out of scope per
the brief's "idle AND grounded only" narrowing).

**Acceptance criteria:**
- [ ] `LookingUp` and `LookingDown` Bool parameters added, default `false`
- [ ] `LookUp` and `LookDown` states added (Motion left empty, assigned in
      Task 3.2)
- [ ] `Idle`'s 3 existing outgoing transitions are byte-identical in order,
      condition, and timing to their pre-plan state — **regression check**
- [ ] `Idle` gains exactly 2 new outgoing transitions, appended after the
      existing 3, single-condition (`LookingUp==true`/`LookingDown==true`
      respectively), duration 0, no exit time
- [ ] `LookUp` has exactly 4 outgoing transitions in the order specified
      above; `LookDown` has exactly 4 outgoing transitions in the
      symmetric order
- [ ] No new transition exists from `Walk`, `JumpAnticipation`, `JumpRise`,
      `JumpFall`, `Attack`, `DefendRaise`, `DefendHold`, `Hurt`, or `Die`
      into `LookUp`/`LookDown`, and none of those states' existing
      transitions were altered — **regression check**
- [ ] The 3 existing Any-State transitions (`DieTrigger`, `HurtTrigger`,
      `AttackTrigger`) are unmodified — confirmed they apply to `LookUp`/
      `LookDown` automatically with zero additional wiring (Mecanim
      Any-State semantics — no per-state opt-in exists or is needed)

---

#### Task 3.2: [GAMEPLAY] Assign `Quirrel_LookUp`/`Quirrel_LookDown` clips to the new states' Motion fields
**Depends on:** Task 3.1, Task 2.1
**Parallel:** no
**Touches:** `Assets/Animations/Quirrel.controller` (Motion field on the 2
new states only)

**Acceptance criteria:**
- [ ] `LookUp` state's Motion = `Quirrel_LookUp` clip; `LookDown` state's
      Motion = `Quirrel_LookDown` clip, by exact name cross-check
- [ ] Controller preview: scrubbing into each new state in the Animator
      window shows the correct sprite, no missing-Motion warning
- [ ] No other state's Motion field touched — **regression check**

---

**Phase 3 dependency graph:**
```
3.1 [GAMEPLAY] topology (independent, parallel with Phases 1/2/4/5) ──┐
                                                                        ├──→ 3.2 [GAMEPLAY] motion assignment
2.1 [ART] clips ────────────────────────────────────────────────────────┘
```

---

## Phase 4 — PlayerController wiring [GAMEPLAY]

#### Task 4.1: [GAMEPLAY] `LookingUp`/`LookingDown` properties, `UpdateLookState`, call site, `Hurt()`/`Die()` force-clears
**Depends on:** none for compiling (Animator `SetBool` on an unknown hash
silently no-ops at runtime — same established pattern every other
parameter write in this file already relies on — so this task does not
functionally require Task 3.1 to have landed first, though the feature
isn't observable end-to-end until it has)
**Parallel:** yes — with Phases 1, 2, 3, 5
**Touches:** `Assets/Scripts/Player/PlayerController.cs` (new properties,
new hash constants, new method, one new call site in `Update()`, small
additions inside the existing `Hurt()`/`Die()` bodies)
**Regression risk:** the new call site sits inside `Update()`, the single
highest-traffic method in this file — must not change the *order* or
*behavior* of any existing line, only append. `Hurt()`/`Die()` already
have several sequential, order-sensitive resets (Section 1.9 of plan
002) — the new `LookingUp`/`LookingDown` clears must be added alongside
the existing `DefendHeld = false` line, not interleaved in a way that
could shadow an existing reset.

Implementation, in words:
- Two new hash constants: `LookingUpHash = Animator.StringToHash
  ("LookingUp")`, `LookingDownHash = Animator.StringToHash("LookingDown")`,
  declared alongside the file's existing 10 hash constants.
- Two new properties: `public bool LookingUp { get; private set; }`,
  `public bool LookingDown { get; private set; }` — same shape as
  `DefendHeld`.
- New method `public void UpdateLookState(bool wHeld, bool sHeld)`:
  computes `bool isIdleAndGrounded = IsGrounded && !IsDead &&
  !IsFullyCommitted && _horizontalInput == 0f;`, then a local
  `+1`/`-1`-accumulation exactly matching `CameraFollow`'s own shape
  (`float lookDirection = 0f; if (wHeld) lookDirection += 1f; if (sHeld)
  lookDirection -= 1f;`), then `LookingUp = isIdleAndGrounded &&
  lookDirection > 0f; LookingDown = isIdleAndGrounded && lookDirection <
  0f;`, then (if `_animator != null`) `SetBool` both hashes to their new
  values.
- Call site: in `Update()`, immediately after the existing `TryJump` block
  and before `UpdateFacing()`/`UpdateAnimatorParameters()` (Decision 4):
  `UpdateLookState(Input.GetKey(KeyCode.W), Input.GetKey(KeyCode.S));`.
- `Hurt()`: alongside the existing `DefendHeld = false;` /
  `_animator.SetBool(DefendHeldHash, false);` lines, add `LookingUp =
  false; LookingDown = false;` and (inside the same `if (_animator !=
  null)` block) `_animator.SetBool(LookingUpHash, false);
  _animator.SetBool(LookingDownHash, false);`.
- `Die()`: identical additions, mirroring `Hurt()`'s for symmetry (same
  convention this file already uses between the two methods).

**Acceptance criteria:**
- [ ] With `IsGrounded` true (default), `IsDead` false, `IsFullyCommitted`
      false, and `_horizontalInput` at its default `0f`: `UpdateLookState
      (true, false)` sets `LookingUp` true, `LookingDown` false;
      `UpdateLookState(false, true)` sets the reverse
- [ ] `UpdateLookState(true, true)` (both held) sets **both** `LookingUp`
      and `LookingDown` false — the accumulation-cancel behavior, mirroring
      `CameraFollow`'s own W/S cancel and this file's own A/D cancel
- [ ] With `IsDead` true: `UpdateLookState(true, false)` leaves both false,
      regardless of which key is passed
- [ ] With `IsFullyCommitted` true (via `DefendHeld` forced true through
      the existing reflection helper): `UpdateLookState(true, false)`
      leaves both false
- [ ] With `IsGrounded` forced false: `UpdateLookState(true, false)` leaves
      both false
- [ ] With `_horizontalInput` forced nonzero (new reflection helper, Task
      6.1): `UpdateLookState(true, false)` leaves both false
- [ ] `Hurt()` called while `LookingUp` (or `LookingDown`) is true
      immediately sets it false, in the same call — not merely on the next
      `Update()` tick
- [ ] `Die()` does the same
- [ ] Existing `Update()` behavior (A/D, J, K, Space handling, facing,
      existing Animator parameter writes) is otherwise byte-identical —
      **regression check**
- [ ] `Hurt()`/`Die()`'s existing reset ordering and all their existing
      acceptance criteria from plan 002 (Section 1.9) remain true — no
      existing reset was reordered or removed — **regression check**

---

**Phase 4 dependency graph:**
```
4.1 [GAMEPLAY] (independent, parallel with Phases 1/2/3/5)
```

---

## Phase 5 — Camera pan distance tuning [GAMEPLAY]

#### Task 5.1: [GAMEPLAY] Scale the pan distance to 40% of screen height
**Depends on:** none
**Parallel:** yes — with everything in Phases 1–4 (different file entirely)
**Touches:** `Assets/Scripts/Camera/CameraFollow.cs` (`Tick()`'s
`panDistance` computation only)
**Regression risk:** this is the one place plan 006's gating logic
(`panBlocked`, `_playerController.IsDead || IsFullyCommitted`) and the
X-axis follow both live — this task must touch **only** the distance
formula, nothing else in `Tick()`. Per Decision 7, this requires **zero**
edit to `Assets/Scenes/SampleScene.unity` — confirmed the scene's
serialized `_panDistanceFallback: 5` value is left exactly as-is and the
new 0.8 multiplier is applied at the read-site, converting it
automatically.

Add `private const float PanDistanceScale = 0.8f;` (documented inline: 40%
of full screen height = 0.8 × orthographicSize, since orthographicSize is
half the vertical viewport in world units). Change `panDistance`'s
computation to `float panDistance = (_camera != null ?
_camera.orthographicSize : _panDistanceFallback) * PanDistanceScale;`. No
other line in `Tick()`, `Update()`, or `EnsureInitialized()` changes.

**Acceptance criteria:**
- [ ] With no `Camera` component present (`_panDistanceFallback` at its
      unchanged default `5f`) and `_panDirection` forced `+1`, camera Y
      converges to baseline `+ 4f` (i.e. `5f * 0.8`), not `+ 5f`
- [ ] With a live `Camera` component (`orthographicSize = 7f`) and
      `_panDirection` forced `+1`, camera Y converges to baseline `+ 5.6f`
      (i.e. `7f * 0.8`), not `+ 7f`
- [ ] The gating logic (`panBlocked`, `IsDead`/`IsFullyCommitted` checks)
      is completely unchanged — **regression check**
- [ ] The X-axis follow and its `0.15s` `SmoothTime` are completely
      unchanged — **regression check**
- [ ] `Assets/Scenes/SampleScene.unity` requires **zero** edits — confirmed
      by re-opening the scene after this change and observing the pan
      distance is already 40%-of-screen live, with the scene's serialized
      `_panDistanceFallback: 5` value untouched

---

**Phase 5 dependency graph:**
```
5.1 [GAMEPLAY] (independent, parallel with Phases 1–4)
```

---

## Phase 6 — QA

#### Task 6.1: [QA] `PlayerController` EditMode tests for `UpdateLookState` and `Hurt()`/`Die()` force-clears
**Depends on:** Task 4.1
**Parallel:** yes — with Task 6.2 (different file), Task 6.3 (different
file); no — internally sequential with nothing else in this file
**Touches:** `Assets/Scripts/Player/Tests/EditMode/PlayerControllerTests.cs`
**Regression risk:** additive only. Adds one new reflection helper
(`ForceSetHorizontalInput`, FieldInfo on `_horizontalInput`) following the
exact convention of the file's existing `ForceSetDefendHeld`/
`ForceSetIsGrounded` helpers.

**Work:**
- `UpdateLookState_WHeldWhileIdleAndGrounded_SetsLookingUpOnly`
- `UpdateLookState_SHeldWhileIdleAndGrounded_SetsLookingDownOnly`
- `UpdateLookState_BothHeld_SetsNeitherTrue_MirrorsCameraCancelBehavior`
- `UpdateLookState_WhileDead_BothFalse_RegardlessOfKeysHeld`
- `UpdateLookState_WhileFullyCommitted_BothFalse` (via
  `ForceSetDefendHeld`, reused from the existing helper)
- `UpdateLookState_WhileNotGrounded_BothFalse` (via `ForceSetIsGrounded`,
  reused)
- `UpdateLookState_WhileHorizontalInputNonzero_BothFalse` (via the new
  `ForceSetHorizontalInput` helper)
- `Hurt_WhileLookingUp_ImmediatelyClearsLookingUp`
- `Die_WhileLookingDown_ImmediatelyClearsLookingDown`

**Acceptance criteria:**
- [ ] All 9 new tests pass
- [ ] The new `ForceSetHorizontalInput` reflection helper is file-scoped
      and documented with the same rationale style as the file's existing
      reflection helpers
- [ ] No test calls `Input.GetKey`/`GetKeyDown` directly — all input
      simulation goes through `UpdateLookState`'s own `bool` parameters or
      the reflection helpers
- [ ] Full existing `PlayerControllerTests` suite (all pre-existing tests)
      still passes unmodified — **regression check**

---

#### Task 6.2: [QA] Extend `AnimatorContractTests` for the new parameters and transition topology
**Depends on:** Task 3.1, Task 3.2, Task 4.1 (needs the real hash fields
and the real controller states/transitions to assert against)
**Parallel:** yes — with Task 6.1 (different file), Task 6.3 (different
file)
**Touches:** `Assets/Scripts/Player/Tests/EditMode/AnimatorContractTests.cs`
**Regression risk:** the existing `ExpectedParameters` array (10 rows) must
be **appended to, not replaced** — all 10 existing rows stay byte-identical
so the existing parameter contract keeps being enforced, not just the new
one.

**Work:**
- Append 2 rows to `ExpectedParameters`:
  `("LookingUpHash", "LookingUp", AnimatorControllerParameterType.Bool)`,
  `("LookingDownHash", "LookingDown", AnimatorControllerParameterType.Bool)`
  — the existing `Controller_HasExactly10Parameters...` test's length
  assertion self-adjusts to 12 with no further change needed (Section 0).
- New test `IdleState_HasExactlyFiveOutgoingTransitions_InOrder_
  IncludingLookUpAndLookDown` — mirrors the existing
  `AttackState_HasExactlyThreeOutgoingTransitions...` test's exact style
  (reusing its `AssertTransition`/`FindStateByName` helpers), asserting
  `Idle`'s 5 transitions in order: `[Walk, IsWalking==true]`,
  `[JumpAnticipation, JumpTrigger]`, `[DefendRaise, DefendHeld==true]`,
  `[LookUp, LookingUp==true]`, `[LookDown, LookingDown==true]`.
- New test `LookUpState_HasExactlyFourOutgoingTransitions_InOrder` —
  asserting `[Walk, IsWalking==true]`, `[JumpAnticipation, JumpTrigger]`,
  `[DefendRaise, DefendHeld==true]`, `[Idle, LookingUp==false]`.
- New test `LookDownState_HasExactlyFourOutgoingTransitions_InOrder` —
  symmetric, using `LookingDown`.

**Acceptance criteria:**
- [ ] All 10 pre-existing `ExpectedParameters` rows are byte-identical to
      today — **regression check**
- [ ] `Controller_HasExactly10Parameters...`'s length assertion now passes
      against 12 actual parameters with no hardcoded number touched
      (renaming the test to reflect "12" is a nice-to-have, not required —
      note either way in the PR/commit so the name doesn't silently lie)
- [ ] All 3 new transition-order tests pass
- [ ] The existing `AttackState_HasExactlyThreeOutgoingTransitions...` test
      still passes unmodified — **regression check**
- [ ] The existing `Controller_ContainsJumpAnticipationState...` test still
      passes unmodified — **regression check**

---

#### Task 6.3: [QA] Update `CameraFollowTests.cs`'s pan-distance assertions for the 0.8 scale
**Depends on:** Task 5.1
**Parallel:** yes — with Task 6.1, Task 6.2 (different file)
**Touches:** `Assets/Scripts/Camera/Tests/EditMode/CameraFollowTests.cs`
**Regression risk:** this task's entire purpose is updating assertions
CLAUDE.md's own brief named explicitly as needing change, not just left to
fail — the risk is updating the *wrong* number or missing one of the five.

**Work — update these 5 existing numeric assertions, and no others:**
- `YPosition_PansUpByFallbackDistance_WhileWDirectionForced`: `3f + 5f` →
  `3f + 4f`
- `YPosition_PansDownByFallbackDistance_WhileSDirectionForced`: `3f - 5f` →
  `3f - 4f`
- `YPosition_ReturnsToBaseline_AfterPanDirectionReleased`: its mid-test
  precondition `3f + 5f` → `3f + 4f` (its final assertion, `3f` baseline,
  is unchanged — releasing pan always returns to baseline regardless of
  scale)
- `YPosition_UsesLiveCameraOrthographicSize_WhenCameraComponentPresent`:
  `3f + 7f` → `3f + 5.6f`
- `PanInput_IsNeverBlocked_WhenPlayerControllerUnassigned`: `3f + 5f` →
  `3f + 4f`
- Update each test's doc comment/assertion message where it states the old
  literal number (`5f`/`7f`), so the message doesn't silently lie about
  what it's asserting

**Acceptance criteria:**
- [ ] All 5 listed assertions updated to the exact new expected values
      above
- [ ] `XPosition_ConvergesToStationaryTarget...`,
      `YAndZPosition_StayFixed_WhileFollowingOnX_AndNoPanInputHeld`,
      `PanInput_IsBlocked_WhilePlayerControllerIsFullyCommitted`,
      `PanInput_IsBlocked_WhilePlayerControllerIsDead` are untouched (none
      of these assert a specific nonzero pan distance, only convergence to
      baseline or X-axis behavior) — **regression check**
- [ ] Full `CameraFollowTests` suite passes, same test count as before
      (9 tests) — no test added or removed by this task, only assertion
      values

---

#### Task 6.4: [QA] Manual playtest protocol addendum + `ART.md` doc sync
**Depends on:** Tasks 1.3, 2.1, 3.2, 4.1, 5.1, 6.1, 6.2, 6.3 (needs the
whole feature working end-to-end, plus the automated suites green, before
a live pass is meaningful)
**Parallel:** no
**Touches:** `Docs/Plans/002_manual-playtest-protocol.md`, `ART.md` §7

**Regression risk:** none to code — documentation only.
`Input.GetKey(KeyCode.W/S)` (both the camera's and the new animation
gate's reads) is exactly the class of behavior the automated suite
structurally cannot verify — a live pass is the only real proof.

Add a new section, following the existing `5a`/`5b` precedent exactly —
call it **`5c. Look up/down idle animation (Docs/Plans/007_look-up-
down-idle-animation-and-pan-tuning.md)`** — with checklist rows for:
looking-up pose shows while standing still and holding W; looking-down
pose shows while standing still and holding S; releasing either returns
cleanly to normal Idle; holding both W and S simultaneously shows neither
look pose (regression pin for the accumulation-cancel design, the
animation's own independent implementation of it — distinct from `5b.4`'s
camera-side check of the same behavior); starting to walk while a look
pose is showing cancels it immediately into Walk; jumping while a look
pose is showing cancels it immediately into the jump sequence; attacking,
defending, or triggering `Hurt()`/`Die()` while a look pose is showing
cancels it immediately (four separate rows, one per interrupt source);
quickly swapping W→S (or S→W) passes through Idle for one frame rather
than swapping directly (Decision 8 — confirm it reads as imperceptible,
not as a visible glitch — escalate as a bug if it doesn't). Also add a
row confirming the camera's own pan (Section `5b`) is **not** newly
restricted by this feature — W/S still pans the camera during Walk/Jump/
etc. exactly as before, even though the animation itself no longer shows
in those states.

Also add a **`5b` addendum row** (not a new section — this is a targeted
update to the existing `5b` section, since the pan distance itself
changed): re-verify `5b.1`/`5b.3` ("pans up/down by a plausible half-screen
amount") now read as **~40%**, not ~50%, of screen height — update the
expected-behavior wording in those two existing rows to say "roughly 40%
of a screen height" instead of "half a screen height," rather than leaving
stale wording that no longer matches the tuned value.

Update `ART.md` §7's Animation Timing table: add one row — `Look Up/Down |
1 each | Cosmetic idle-only overlay, shown while W/S held and the
character is idle+grounded; cancels immediately on any other action`.

**Acceptance criteria:**
- [ ] New `5c` section added with all rows listed above, following the
      `5a`/`5b` format exactly
- [ ] `5b.1`/`5b.3`'s wording updated from "half a screen height" to
      "roughly 40% of a screen height" — no other `5b` row content altered
- [ ] Live verification performed and recorded PASS/FAIL for every new
      `5c` row and the `5b.1`/`5b.3` re-verification
- [ ] Any FAIL is logged as a bug per this doc's existing bug-report
      format and routed back through the pipeline, not silently patched
      inside this task
- [ ] `ART.md` §7's Animation Timing table has exactly one new row added;
      no other `ART.md` content altered — **regression check** (diff
      confirms only this one row changed)
- [ ] Re-confirm the existing `5a` (air-attack) and the rest of `5b`
      (full-commit/dead gating rows) still read correctly — **regression
      check**

---

**Phase 6 dependency graph:**
```
4.1 ──→ 6.1 [QA] PlayerController tests ─────────┐
3.1, 3.2, 4.1 ──→ 6.2 [QA] AnimatorContractTests ─┼──→ 6.4 [QA] manual playtest + ART.md sync
5.1 ──→ 6.3 [QA] CameraFollowTests update ────────┘
(6.1/6.2/6.3 mutually parallel — three different files)
```

---

## Explicitly out of scope for this plan
"Look while walking" or "look while airborne" variants (narrow scope is
explicit and confirmed with the user); a direct `LookUp`↔`LookDown`
Animator transition (Decision 8); any change to `CameraFollow`'s gating
condition (`IsDead || IsFullyCommitted`) — only its pan *distance*; any
Input System migration (still legacy `KeyCode`/`Input.GetKey`); any
save/ScriptableObject schema; any tags/layers/physics-matrix change; any
scene edit (Decision 7, Section 0); rebindable look/pan keys; sprite
atlasing/packing; any change to the original 24-frame sheet or its
existing clips/states/transitions beyond the additive edges named in
Phase 3.

---

## Judgment calls made explicit

1. A new, small, single-purpose Editor tool
   (`QuirrelReferenceSpriteImporter.cs`) rather than extending the
   sheet-specific slicer or hand-cropping outside the pipeline (Decision
   1).
2. Resize happens before knockout/feathering, not after, to avoid diluting
   the 1px feather ring across a ~9× downscale (Decision 2).
3. `PlayerController` reads its own independent copy of W/S input rather
   than sharing state with `CameraFollow` — same physical keys, two
   independent reads, by design (Decision 3).
4. `UpdateLookState`'s call site is placed after every other input handler
   in `Update()`, so locally-triggered Attack/Defend/Walk interrupts cancel
   a look-pose in the same frame via the code-side gate; Jump does **not**
   same-frame-cancel through that gate (`IsGrounded` only flips via
   `FixedUpdate` and stays true through the jump-start frame) and is
   instead masked by Animator transition-list priority (`JumpTrigger`
   evaluated ahead of `LookingUp`/`LookingDown` on every relevant state);
   externally-triggered `Hurt()`/`Die()` calls are handled by an explicit
   force-clear instead, mirroring the existing `DefendHeld` precedent
   exactly (Decisions 4–5, 10).
5. `UpdateLookState` is public and parameter-driven (majority convention in
   this file) rather than inlined into `Update()` like `DefendHeld` (the
   one outlier) — maximizes EditMode-testable surface (Decision 6).
6. Camera pan-distance scale is a single multiplier applied at the
   read-site to both the live-orthographic-size and fallback paths,
   leaving `_panDistanceFallback`'s stored default and the scene's
   serialized value completely untouched — zero scene edits required
   (Decision 7).
7. No direct `LookUp`↔`LookDown` Animator transition — a quick W→S swap
   passes through `Idle` for one frame, an accepted minor simplification
   given the feature's explicitly narrow scope (Decision 8).
8. No redundant `IsGrounded` Animator-side condition on the new
   transitions — mirrors the established `DefendHeld` convention, since
   the code-side gate already does the real work (Decision 9).
9. The 0.08s jump-anticipation window's brief `LookingUp`/`LookingDown`
   code-side "staleness" while airborne-but-still-`IsGrounded==true` is a
   considered-and-dismissed no-op, not a bug (Decision 10).

---

## Reference file paths consulted while drafting this plan

- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\CLAUDE.md`
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\ART.md`
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Docs\Plans\002_quirrel-sprite-animation-player-control.md`
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Docs\Plans\002_manual-playtest-protocol.md`
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Docs\Plans\006_look-up-down-camera-pan.md`
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Docs\Backlog.md`
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Assets\Scripts\Player\PlayerController.cs`
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Assets\Scripts\Player\Tests\EditMode\PlayerControllerTests.cs`
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Assets\Scripts\Player\Tests\EditMode\AnimatorContractTests.cs`
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Assets\Scripts\Camera\CameraFollow.cs`
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Assets\Scripts\Camera\Tests\EditMode\CameraFollowTests.cs`
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Assets\Scripts\Camera\Quirrel.Camera.asmdef`
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Assets\Scripts\Player\Quirrel.Player.asmdef`
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Assets\Animations\Quirrel.controller`
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Assets\Editor\Tools\QuirrelSpriteKnockout.cs`
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Assets\Editor\Tools\QuirrelSpriteSlicer.cs`
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Assets\Sprites\Quirrel\Idle\Quirrel_Idle_01.png.meta`
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Assets\Sprites\Quirrel\Idle\Quirrel_Idle_02.png.meta`
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Assets\Sprites\Reference\Quirrel_Looking_Up.png` (100×141px, read directly)
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Assets\Sprites\Reference\Quirrel_Looking_Down.png` (896×1174px, read directly)
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Assets\Scenes\SampleScene.unity` (grepped for the `CameraFollow` component block, confirmed `_panDistanceFallback: 5` serialized)
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\ProjectSettings\ProjectVersion.txt` (confirmed Unity 2021.3.45f2)
