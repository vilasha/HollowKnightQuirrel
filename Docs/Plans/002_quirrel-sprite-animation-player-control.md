# Implementation Plan — Quirrel Sprite Pipeline, Animation, and Player Control

**Status:** ✅ ACCEPTED (implementation-plan-reviewer, four review rounds — see history below). Ready for implementation.

**Review history:** Drafted by implementation-plan-architect, then reviewed by implementation-plan-reviewer across four rounds:
- Round 1: NEEDS REVISION — 5 Critical + 6 Major findings (Hurt state had no exit transition and would freeze the character forever; Task 1.2 mixed ART/engineering disciplines; Task 3.2 was oversized; the jump-anticipation timing was ambiguous; no guard against re-triggering jump mid-anticipation; missing Build Verification phase; Attack/Defend could show the wrong pose while airborne; Attack could stack on rapid presses; Hurt re-entrancy was undefined; player collider alignment wasn't verified; velocity-write component preservation wasn't specified).
- Round 2: REJECT pending 2 Required changes — fixing the above exposed a real gap: the delayed jump impulse wasn't cancelled if Hurt/Die interrupted the jump (could launch a dead character's corpse), and Attack/Defend only gated Jump input, not horizontal movement, so the character could still walk off the ground's edge mid-attack.
- Round 3: REJECT pending 2 Required changes — fixing Round 2's issues introduced a new bug class: guard flags (`_jumpInProgress`, `_isAttacking`) that only cleared on natural state completion could get stuck permanently `true` if Hurt/Die interrupted first, silently disabling jump or attack for the rest of the session. Also found an undeclared dependency (Task 3.4 needed the Attack clip from Task 2.3 to exist for its Animation Event).
- Round 4: ACCEPT WITH MINOR REVISIONS — no new functional bugs found; two mechanical fixes applied (a missing regression-check bullet, and splitting an overloaded task into production-code and test-suite halves).

**Why this history is preserved here:** several of the fixes above are non-obvious and easy to silently regress if a future change touches the same code without this context (e.g. re-introducing a code timer for `_isAttacking` without also re-adding the interrupt-reset would reopen Round 3's bug). Treat this plan as the record of *why*, not just *what*.

**Author:** implementation-plan-architect, reviewed by implementation-plan-reviewer
**Date:** 2026-08-04

---

## 0. Summary

Cut the 24 verified frames out of `Assets/Sprites/Quirrel_Sprites.png`, give them real alpha transparency, assemble them into 10 AnimationClips across 7 gameplay verbs (Idle, Walk, Jump, Attack, Defend, Hurt, Die), wire an AnimatorController with a 10-parameter contract, and drive it with a keyboard-controlled `PlayerController` that also exposes public trigger APIs for external `Hurt`/`Die` events. Land the result, playable, in `Assets/Scenes/SampleScene.unity`, and confirm it survives a real Standalone build, not just Editor Play mode.

This is the **first player-facing system** in the project (zero scripts, zero asmdefs, zero physics layers, zero AnimatorControllers exist today). Every foundational decision below is being made once, here, and will be expensive to change later — treated with that weight.

**Control scheme (locked in before drafting began):**
- Idle: default state, no input
- Walk: Left/Right arrow keys, moves the character horizontally on screen
- Jump: Space. Space alone = vertical jump. Space + Left/Right held = jump with horizontal movement in that direction.
- Attack: Z key, fires once per press (not held)
- Defend: X key, held for duration of the pose
- Hurt: triggered by an external event/method call (`Hurt()`), no in-game damage system exists yet — this plan implements the animation + a public trigger API only
- Die: triggered by an external event/method call (`Die()`), same caveat as Hurt

---

## 1. Key Technical Decisions

Read against `ART.md` §7 baseline (PPU 100, Pivot Center, Bilinear, Max Size 2048, sRGB on, Alpha Is Transparency on) and the verified 24-frame map in `Docs/Plans/001_pin-head-recolor.md` §1.4–1.5.

### 1.1 Sprite import — deviations from the sheet-level baseline, justified
| Setting | Value | Rationale |
|---|---|---|
| Pixels Per Unit | **100** | Matches frozen baseline. Do not change. |
| Pivot | **Bottom (0.5, 0)**, not Center | Per-frame bounding boxes are tight crops with varying heights (Idle ≈131px, Jump crouch 122px vs Jump apex 142px, etc.). A Center pivot would make the character bob vertically frame-to-frame. Bottom pivot keeps feet glued to a constant ground line. |
| Max Size | **256** | Largest single frame is 245×142px; 256 is the smallest power-of-two that fits everything, vs. the sheet-level 2048. |
| Filter Mode | **Bilinear** | Matches baseline — painted character frames at PPU 100, not blocky pixel art. |
| Alpha Is Transparency | **On** | Matches baseline; now functionally meaningful after knockout. |
| sRGB (Color Texture) | **On** | Matches baseline. |
| Sprite Mode | **Single** per frame file | Matches the user's explicit "frame per file" instruction — one PNG, one Sprite, per frame. |

### 1.2 Background knockout technique
The sheet is currently 100% opaque white (alpha=255 everywhere). A naive global luminance threshold is rejected: the character has enclosed near-white regions (eye whites, hood highlights) that a flat threshold risks punching holes through.

**Decision:** border-connected flood fill, not global thresholding.
- From the four canvas edges, flood-fill (4-connected) through pixels with R,G,B ≥ 245, marking only the outer background region reachable from the border as transparent (alpha 0).
- Enclosed near-white regions are topologically disconnected from the border region and left untouched.
- A 1px alpha feather (linear ramp) is applied at the resulting cutout boundary to avoid a hard white fringe at bilinear-filtered edges.
- Tool: a one-off Unity Editor script under `Assets/Editor/Tools/`. Kept (not thrown away) in case new frames are added later.
- Writing this algorithm is engineering, not art judgment — it is its own `[GAMEPLAY]`-tagged task (1.2), separate from the `[ART]` task that runs it and visually accepts/rejects the output (1.3).

### 1.3 File layout — iterate off-tree, promote once
Intermediate knockout/crop attempts happen in the scratchpad, not under `Assets/`. Only the final 24 accepted PNGs are written into the repo, directly to their final path — no throwaway iteration commits to LFS (`.gitattributes` confirms `*.png` is LFS-tracked repo-wide).

Final layout:
```
Assets/Sprites/Quirrel/
  Idle/   Quirrel_Idle_01.png … _04.png
  Walk/   Quirrel_Walk_01.png … _06.png
  Jump/   Quirrel_Jump_01_Crouch.png, _02_Apex.png, _03_Descend.png
  Attack/ Quirrel_Attack_01.png … _04.png
  Defend/ Quirrel_Defend_01_PreRaise.png, _02_Raise.png, _03_Held.png
  Hurt/   Quirrel_Hurt_01.png
  Die/    Quirrel_Die_01.png … _03.png
```
`Assets/Sprites/Quirrel_Sprites.png` itself is **not modified** — it remains the archival source, only read from.

### 1.4 Jump — 3-frame constraint, timing, and interrupt safety
**Verdict: 3 frames is sufficient. No new art needed.** Built as a 3-phase Animator sub-state-machine, not a single flipbook clip:
- **JumpAnticipation** (F11, crouch) — held a fixed **0.08s** before the launch impulse is applied.
- **JumpRise** (F12, apex pose) — active for the entire ascent, condition-driven (`VerticalVelocity > 0.1`).
- **JumpFall** (F13, descend pose) — active for the entire descent, condition-driven (`IsGrounded == false && VerticalVelocity <= 0.1`).

**Timing/behavior:**
- On Space press while grounded and not already mid-jump, `JumpTrigger` fires immediately (Animator enters `JumpAnticipation` immediately) and a 0.08s code-side timer starts.
- Horizontal movement is **not frozen** during the 0.08s window — normal walk-speed horizontal input continues to apply, so a running jump carries horizontal momentum into the anticipation beat.
- The vertical impulse (`v0 = 15 u/s`) is applied exactly when the 0.08s timer elapses, matching the `JumpAnticipation` clip's own length — not on the initial Space press.
- **Jump re-trigger guard:** a code-side `_jumpInProgress` bool is set `true` the moment `JumpTrigger` fires. Space presses are ignored whenever `_jumpInProgress` is true, regardless of `IsGrounded` (since `IsGrounded` alone stays true throughout the 0.08s anticipation window and would not otherwise prevent a second press from re-entering the jump-start path).
- **Delayed-impulse cancellation:** Hurt/Die are Any-State transitions with `Interruption Source = Current State`, so they can cut into `JumpAnticipation` before its 0.08s timer elapses. The delayed-impulse callback checks `IsDead` and whether `JumpAnticipation` is still the active Animator state at the moment it fires; it no-ops if either fails — this covers both the Die case and a Hurt-interrupt case with one check, without needing separate logic for each.
- **`_jumpInProgress` clearing:** its *normal* clear condition is landing (`IsGrounded` false→true). **This alone is not sufficient**: if Hurt interrupts during the 0.08s window, the impulse is correctly cancelled *before* it would have lifted the character off the ground — so the character never leaves the ground at all, and there is no landing edge to clear the flag on. Left unfixed, `_jumpInProgress` would stay `true` permanently, silently disabling jump for the rest of the session after any hit landed in that ~80ms window. **Fix: `_jumpInProgress` is explicitly reset to `false` inside `Hurt()` and `Die()` at the moment the interrupt is accepted** (Task 3.4a), independent of and in addition to the normal landing-based clear in Task 3.3.

### 1.5 Movement & physics numbers (verified correct by hand during review — worked: `v0 = 2h/t = 2(3.0)/0.4 = 15`; `g = v0/t = 15/0.4 = 37.5`; check `h = v0²/(2g) = 225/75 = 3.0` ✓; `t_apex = v0/g = 15/37.5 = 0.4` ✓; `gravityScale = 37.5/9.81 ≈ 3.823` ✓)
| Value | Number | Derivation |
|---|---|---|
| Walk speed | **4.5 units/sec** | ≈3.5× character height/sec — brisk, controlled |
| Air horizontal control | **4.5 units/sec** (same as walk) | Full air control, no momentum blending, v1 simplicity |
| Jump apex height | **3.0 units** | ≈2.3× character height |
| Time to apex | **0.4 sec** | Snappy, HK-like, not floaty |
| Initial jump velocity (v0) | **15 units/sec** | `v0 = 2h/t` |
| Gravity magnitude | **37.5 units/sec²** | `g = 2h/t²`; as `Rigidbody2D.gravityScale ≈ 3.823` against Unity's default `Physics2D.gravity = -9.81` |
| Terminal fall velocity clamp | **-20 units/sec** | Defensive clamp only; not reached within a normal symmetric jump arc (max fall velocity = v0 = 15) |
| Total airtime (symmetric) | **≈0.8 sec** | Rise + fall, single constant gravity (no asymmetric fall gravity in v1 — see §1.11) |
| Physics driver | `Rigidbody2D` (Dynamic), velocity assigned directly (not `AddForce`) | Tight, deterministic platformer control |
| Input/physics loop | Input read in `Update` (to not miss `GetKeyDown` edges); velocity applied **and grounded-check performed in `FixedUpdate`** | Standard Unity correctness pattern; grounded-check belongs alongside the physics step it gates |
| Velocity-write discipline | Horizontal writes set only `.x` and preserve the existing `.y` (e.g. `rb.velocity = new Vector2(desiredX, rb.velocity.y)`); vertical jump impulse sets only `.y` and preserves the existing `.x`. Neither write may stomp the other component. | Common Unity 2D bug otherwise |

### 1.6 Animation clip authoring (10 clips from 24 frames)
| Clip | Frames | Loop | Length / hold | FPS derivation |
|---|---|---|---|---|
| Quirrel_Idle | F1–F4 | Yes | 0.667s | 6 fps — slow, breathing |
| Quirrel_Walk | F5–F10 | Yes | 0.5s | 12 fps — full step cycle at 4.5 u/s |
| Quirrel_JumpAnticipation | F11 | No | 0.08s fixed hold | single-frame, duplicate-keyframed (§ below) |
| Quirrel_JumpRise | F12 | Yes (condition-exits) | n/a | single-frame, held by Animator condition not clip length |
| Quirrel_JumpFall | F13 | Yes (condition-exits) | n/a | single-frame, held by Animator condition |
| Quirrel_Attack | F15–F18 | No | 0.25s total | 16 fps — fast, punchy nail-swing feel |
| Quirrel_DefendRaise | F19→F20→F21 | No | 0.15s total | 20 fps — quick raise |
| Quirrel_DefendHold | F21 | Yes | n/a — held while X down | single-frame, duplicate-keyframed, shares F21 sprite with DefendRaise's last frame |
| Quirrel_Hurt | F14 | Yes (code-timed) | 0.3s hit-stun | single-frame, duplicate-keyframed; exit via `HurtRecoveryTrigger`, not clip length or Exit Time |
| Quirrel_Die | F22→F23→F24 | No | F22: 0–0.15s, F23: 0.15–0.5s, F24: held indefinitely | uneven custom keyframe timing — a slow weighty collapse, not a uniform framerate |

Fixed-duration single-frame clips (`JumpAnticipation`, `DefendHold`, `Hurt`) use the standard duplicate-keyframe technique — the single sprite is keyed at both the start and end of the clip's timeline so the clip has a real, non-zero length for Unity's Exit Time / loop machinery to operate on.

### 1.7 Animator parameter contract (10 parameters — fixes the interface so ART/GAMEPLAY tasks can run in parallel without touching each other's files)
| Parameter | Type | Set by |
|---|---|---|
| `IsWalking` | bool | PlayerController, true while grounded, horizontal input nonzero, and not committed to Attack/Defend (§1.8) |
| `IsGrounded` | bool | PlayerController, ground-check result (`FixedUpdate`) |
| `VerticalVelocity` | float | PlayerController, `Rigidbody2D.velocity.y`, signed |
| `JumpTrigger` | trigger | PlayerController, on Space press while grounded and not `_jumpInProgress` |
| `AttackTrigger` | trigger | PlayerController, on Z press (edge, not held), gated off while `DefendHeld`, `IsDead`, `!IsGrounded`, or `_isAttacking` |
| `DefendHeld` | bool | PlayerController, true while X held, gated off while `IsDead` or `!IsGrounded` |
| `HurtTrigger` | trigger | Public `Hurt()` API, gated off while `IsDead` |
| `HurtRecoveryTrigger` | trigger | PlayerController, fired the instant the 0.3s hit-stun timer elapses |
| `DieTrigger` | trigger | Public `Die()` API |
| `IsDead` | bool | Set true permanently the first time `DieTrigger` fires; guards all other input-driven triggers in code |

Note on discrete input: `IsWalking` is a bool, not a float `Speed` — keyboard-only digital input has no analog magnitude to represent.

**Transition topology:**
- Locomotion: Idle ↔ Walk on `IsWalking`.
- Jump sub-graph: Locomotion → JumpAnticipation (`JumpTrigger`) → JumpRise (`VerticalVelocity > 0.1`) → JumpFall (`VerticalVelocity <= 0.1`) → Locomotion (`IsGrounded == true`).
- Attack: Any State → Attack (`AttackTrigger`, `Can Transition To Self = false`) → Locomotion (Exit Time = 1). Grounded-only, and the character cannot leave the ground while in this state (§1.8), so no airborne-exit branching is needed.
- Defend: Locomotion → DefendRaise (`DefendHeld == true`) → DefendHold (Exit Time from Raise) → Locomotion (`DefendHeld == false`). Same grounded-only guarantee.
- Hurt: Any State → Hurt (`HurtTrigger`), `Can Transition To Self = true`, `Interruption Source = Current State` (above Attack/Defend in priority) → Hurt → Locomotion (`HurtRecoveryTrigger` AND `IsGrounded == true`) / Hurt → JumpFall (`HurtRecoveryTrigger` AND `IsGrounded == false`) — two exit branches, because unlike Attack/Defend, Hurt is externally triggered and can legitimately happen mid-air.
- Die: Any State → Die (`DieTrigger`), highest priority, no outgoing transitions.

### 1.8 Attack/Defend "commit in place" decision
Attack and Defend are both reachable from Any State, above the Jump sub-graph in priority. The ground plane is finite (30 units, §1.15) — if horizontal movement were still allowed during Attack/Defend, the character could walk off the ground's edge mid-swing/mid-block and become airborne while in a state whose exit transition assumes grounded.

**Fix: Attack and Defend fully commit the character in place.** While `_isAttacking` or `DefendHeld` is true:
- Both Jump/Space **and** Left/Right horizontal input are ignored — the character does not move and cannot leave the ground.
- `AttackTrigger`/`DefendHeld` themselves can still only be entered while grounded (unchanged).

This is a better game-feel choice as well as the correctness fix: Attack and Defend read as genuine committed actions, matching the genre convention this plan already follows elsewhere. With horizontal movement also locked, the character provably cannot leave the ground while in either state on this flat demo plane, so Task 3.5's decision not to add a redundant `IsGrounded` branch to Attack/Defend's exit transitions is correct rather than merely assumed.

**Attack no-stack guard:** `Can Transition To Self = false` (Animator, Task 3.5) plus a code-side `_isAttacking` bool (Task 3.4a) that blocks re-firing `AttackTrigger` while true — defense in depth, not contradictory, since the code guard is at least as conservative as the Animator guard. `_isAttacking` clears via an Animation Event on the Attack clip's final frame on the *normal completion* path (tighter sync than a code timer) — see §1.9 for the interrupt case.

### 1.9 Guard-flag reset on interrupt
**Root cause, generalized:** a guard flag that is only ever cleared by "its owning state finished naturally" breaks the instant that state is interrupted rather than completed — because an Any-State interrupt (Hurt/Die, by design outranking both Attack and the Jump sub-graph) skips the natural-completion signal entirely.

Two instances, both fixed the same way:
- **`_jumpInProgress`** (§1.4): if Hurt interrupts during the 0.08s `JumpAnticipation` window, the delayed impulse is correctly cancelled — but that means the character never left the ground, so there's no landing edge to clear the flag on.
- **`_isAttacking`** (§1.8): if Hurt/Die interrupts Attack mid-swing, the clip's final frame — and therefore its Animation Event — is never reached.

**Fix, both instances:** `Hurt()` and `Die()` explicitly reset `_jumpInProgress = false` and `_isAttacking = false` at the moment the interrupt is accepted (Task 3.4a), rather than relying on either flag's owning state to signal completion. This is unconditional — harmless when the flag is already false — and avoids needing to track *which* state was interrupted.

`DefendHeld` does not have this problem — it's a continuous read of live input state each frame, not a one-way latch. As a belt-and-suspenders touch for immediate visual consistency (not required for correctness), `Hurt()`/`Die()` also explicitly set `DefendHeld = false` on interrupt.

**Race-safety note (confirmed during review, worth preserving):** both clear-paths (Task 3.3's normal landing-edge clear, Task 3.4a's interrupt clear) only ever *write* `false`, never `true` — the only writer of `true` is the single `JumpTrigger`-fire site in Task 3.3, which itself requires the flag to currently be `false`. This makes the two clear-paths idempotent and commutative regardless of execution order or same-frame overlap — a genuinely race-free design by construction, not by luck. Preserve this property if either clear-path is ever touched again.

### 1.10 Hurt re-entrancy decision
If `Hurt()` is called again while a hit-stun window is already active, the 0.3s timer restarts (extending stun) rather than being ignored or queued. There is only ever one active hit-stun timer. `HurtRecoveryTrigger` fires once per completed **stun episode** (i.e., once after the timer finally runs out uninterrupted), not once per individual `Hurt()` call.

### 1.11 Explicit deferrals (named per this project's requirement to flag judgment calls, not silently assume)
- **Coyote time / jump-buffering** — not requested, not needed to satisfy the literal spec. Deferred as a fast-follow.
- **Asymmetric fall gravity** — deferred; v1 uses one constant gravity value for both rise and fall.
- **Defend lower/release animation** — releasing X snaps directly back to locomotion; no reverse `DefendRaise` playback on release.
- **Landing squash / attack-recovery pose** — no such frames exist in the source art.

### 1.12 Input system
No `com.unity.inputsystem` package is installed (confirmed via `Packages/manifest.json`). **Decision: use the legacy `UnityEngine.Input` API** (`Input.GetKey`/`GetKeyDown`), hardcoded to `LeftArrow`/`RightArrow`/`Space`/`Z`/`X`. Adopting the new Input System is out of scope — key rebinding will require a code change in v1, not a data asset, noted as a known limitation.

### 1.13 Physics layers — first ones ever added to this project
`ProjectSettings/TagManager.asset` currently has zero custom tags or layers. This plan adds two: **Player** (layer 8) and **Ground** (layer 9). Flagged per this project's regression-risk rules as a global, silent change.

### 1.14 Facing direction
The source art only faces one direction (screen-right). Left-facing movement is achieved via `SpriteRenderer.flipX`, not mirrored art frames, driven by last-nonzero horizontal input direction and persisting through Attack/Defend/Hurt.

### 1.15 Camera & ground plane (minimal, explicitly not final art/level design)
- Ground: a single `BoxCollider2D` on a placeholder-tinted `GameObject` named `Ground`, layer `Ground`, width 30 units, top surface at world `y = 0` — aligned with the Bottom-pivot sprite convention (§1.1).
- Player spawn: `(0, 0, 0)`.
- Camera: `Main Camera`, orthographic size **5**, a `CameraFollow` script does X-only `SmoothDamp` with `smoothTime = 0.15s`, Y and Z fixed (`Z = -10`). No Cinemachine (not installed; out of scope).

### 1.16 Persisted state
None. No `[DATA]` tasks. Movement/timing constants stay as serialized fields on `PlayerController` for v1 rather than a ScriptableObject tuning asset.

### 1.17 Assembly structure
```
Assets/Scripts/Player/Quirrel.Player.asmdef                         (runtime)
Assets/Scripts/Player/Tests/EditMode/Quirrel.Player.EditModeTests.asmdef
Assets/Scripts/Player/Tests/PlayMode/Quirrel.Player.PlayModeTests.asmdef
```
Set up as part of the first script task (3.2), with correct cross-references.

### 1.18 Build target
`Packages/manifest.json` confirms `com.unity.toolchain.win-x86_64-linux-x86_64` is installed — this project can cross-build for Linux, not just Windows. First player-facing system, first build-and-run smoke check belongs in the plan (Phase 5), not just Editor Play mode.

---

## 2. Explicitly Out of Scope

- Real combat/health/damage system (no health pool, no hit detection — `Hurt`/`Die` are trigger-API stubs only)
- Actual game-over flow beyond Die's animation. **Input-lock after Die IS in scope** (`IsDead` gate) — near-zero cost, prevents a visibly broken "walking around mid-death-pose" state. No respawn/game-over UI.
- Enemy AI
- Level design / tilemap / real ground collision beyond the single minimal ground plane
- Save/load, persisted progression
- UI/HUD
- Camera work beyond the minimal X-follow
- Sprite atlasing/packing for build optimization
- Any changes to the completed pin-head recolor work
- Coyote time, jump buffering, asymmetric fall gravity, Defend release animation, landing squash (§1.11)
- New Input System package adoption (§1.12)
- Airborne Attack/Defend (grounded-only, enforced per §1.8)
- Automated Linux Play-mode verification (build-only smoke check per §1.18 — no Linux runtime environment available to this pipeline)

---

## 3. Blast Radius (pre-existing systems this plan touches)

- `ART.md` §7 (frame-count table is currently wrong — 34 vs actual 24 — corrected here)
- `Assets/Scenes/SampleScene.unity` (untouched default scene — gets a Player, Ground, and camera script wired in)
- `ProjectSettings/TagManager.asset` (first-ever layers added — Player, Ground)
- `Assets/Sprites/Quirrel_Sprites.png` — **read-only source**, not modified

Everything else created by this plan is new — `Touches: none (new system)` unless stated otherwise.

---

## Phase 1 — Sprite Pipeline

#### Task 1.1: [ART] Correct ART.md §7 frame-count table
**Depends on:** none · **Parallel:** yes — with everything in Phase 1 · **Touches:** `ART.md` §7 · **Regression risk:** documentation only

**Acceptance criteria:**
- [ ] §7's frame-count table replaced with the verified 24-frame breakdown (Idle 4, Walk 6, Jump 3, Hurt 1, Attack 4, Defend 3, Die 3) matching `Docs/Plans/001_pin-head-recolor.md` §1.4
- [ ] A short note added to §7's sprite import baseline documenting the Bottom-pivot deviation for per-frame character sprites and why
- [ ] No other ART.md content altered
- [ ] Regression check: diff confirms only the frame-count table and the new pivot note changed

#### Task 1.2: [GAMEPLAY] Build the slicer/knockout Editor tool
**Depends on:** none · **Parallel:** yes — with Task 1.1 · **Touches:** none (new system) — creates `Assets/Editor/Tools/QuirrelSpriteSlicer.cs`
**Regression risk:** none (new system). Writing the flood-fill/feathering algorithm is engineering, kept separate from the `[ART]` acceptance task (1.3).

**Acceptance criteria:**
- [ ] Editor menu-item tool implements border-connected 4-connected flood-fill knockout (§1.2), threshold R,G,B ≥ 245, starting from canvas edges
- [ ] 1px alpha feather applied at the resulting cutout boundary
- [ ] Tool accepts the 24 verified bbox rects (from `Docs/Plans/001_pin-head-recolor.md` §1.5) and crops+processes each into an independent RGBA PNG, writable to an arbitrary output folder (scratchpad first, per §1.3)
- [ ] Verified against a synthetic test texture (a white square with a solid-color island and a separate enclosed white "eye" region inside a darker shape): only border-connected white is knocked out, enclosed white survives
- [ ] Tool lives under `Assets/Editor/` (excluded from player builds)

#### Task 1.3: [ART] Run the slicer and accept/reject the 24 frame outputs
**Depends on:** Task 1.2 · **Parallel:** no · **Touches:** `Assets/Sprites/Quirrel_Sprites.png` (read-only, not modified); creates `Assets/Sprites/Quirrel/**` (new)
**Regression risk:** none (new system) — future regression surface: any later change to `Quirrel_Sprites.png` must go back through this same tool/process to stay consistent

**Acceptance criteria:**
- [ ] Run the Task 1.2 tool against all 24 frame rects, output directed to the scratchpad first (off-tree iteration, §1.3)
- [ ] All 24 frame rects match the bbox table in `Docs/Plans/001_pin-head-recolor.md` §1.5 (spot-check: crop each frame and confirm no adjacent frame content is included, no content clipped at the crop edge)
- [ ] Each of the 24 output PNGs visually inspected at ≥400% zoom against a contrasting checkerboard background: no white halo/fringe at edges, no unintended holes inside the character silhouette
- [ ] 9 label text strings ("IDLE:", "WALK:", etc.) confirmed absent from every output frame
- [ ] Only the 24 final accepted PNGs are promoted into `Assets/Sprites/Quirrel/**` per the layout in §1.3 — no throwaway PNGs committed to Assets/ (LFS churn discipline)
- [ ] Any frame that fails visual QA is round-tripped back to Task 1.2 for a tool fix or a manual override rect, not silently accepted

#### Task 1.4: [ART] Unity sprite import configuration for the 24 frame files
**Depends on:** Task 1.3 · **Parallel:** no · **Touches:** none (new system) — new `.meta` files only

**Acceptance criteria:**
- [ ] All 24 sprites: Sprite Mode Single, PPU 100, Pivot Bottom (0.5, 0), Max Size 256, Filter Bilinear, sRGB on, Alpha Is Transparency on (§1.1)
- [ ] Visual spot-check in Unity's Sprite Editor: for at least one Walk and one Jump frame, confirm the character's feet/lowest visible pixel sits at the sprite's local y=0
- [ ] All 24 sprites organized into the per-state folders from §1.3

---

**Phase 1 dependency graph:**
```
1.1 [ART] docs fix ─────────────────────────(independent, parallel with all)
1.2 [GAMEPLAY] build slicer tool ──→ 1.3 [ART] run + accept 24 frames ──→ 1.4 [ART] import config
```

---

## Phase 2 — Animation Clips [ART]

#### Task 2.1: [ART] Idle and Walk AnimationClips
**Depends on:** Task 1.4 · **Parallel:** yes — with 2.2, 2.3 · **Touches:** none (new system)

**Acceptance criteria:**
- [ ] `Quirrel_Idle`: F1–F4, Loop Time on, 6 fps, 0.667s clip length (§1.6)
- [ ] `Quirrel_Walk`: F5–F10, Loop Time on, 12 fps, 0.5s clip length
- [ ] Both clips play back smoothly in the Animation window preview with no frame popping/misordering

#### Task 2.2: [ART] Jump 3-phase pose clips
**Depends on:** Task 1.4 · **Parallel:** yes — with 2.1, 2.3 · **Touches:** none (new system)

**Acceptance criteria:**
- [ ] `Quirrel_JumpAnticipation`: F11 only, duplicate-keyframed to 0.08s (matches the code-side timer in §1.4 exactly)
- [ ] `Quirrel_JumpRise`: F12 only, Loop Time on
- [ ] `Quirrel_JumpFall`: F13 only, Loop Time on
- [ ] Clip names exactly match §1.6's table (Task 3.6 depends on exact naming)

#### Task 2.3: [ART] Attack, Defend, Hurt, Die AnimationClips
**Depends on:** Task 1.4 · **Parallel:** yes — with 2.1, 2.2 · **Touches:** none (new system)

**Acceptance criteria:**
- [ ] `Quirrel_Attack`: F15–F18, Loop Time off, 16 fps, 0.25s total — **this clip is a hard prerequisite for Task 3.4a's Animation Event** (see Task 3.4a's dependency)
- [ ] `Quirrel_DefendRaise`: F19→F20→F21, Loop Time off, 20 fps, 0.15s total
- [ ] `Quirrel_DefendHold`: F21 only, duplicate-keyframed, Loop Time on
- [ ] `Quirrel_Hurt`: F14 only, duplicate-keyframed, Loop Time on
- [ ] `Quirrel_Die`: F22 (0–0.15s) → F23 (0.15–0.5s) → F24 (held, Loop Time off), custom keyframe timing per §1.6
- [ ] All 10 clip names across Tasks 2.1–2.3 match the §1.6 table exactly

---

**Phase 2 dependency graph:**
```
1.4 [ART] ──┬──→ 2.1 [ART] Idle/Walk ───┐
            ├──→ 2.2 [ART] Jump phases ─┼──→ (Task 3.6, motion assignment)
            └──→ 2.3 [ART] Attack/Defend/Hurt/Die ─┴──→ (Task 3.4a, Animation Event)
```

---

## Phase 3 — Animator Controller + Player Controller [GAMEPLAY]

#### Task 3.1: [GAMEPLAY] Add Player and Ground physics layers
**Depends on:** none · **Parallel:** yes, sequenced first in this phase since 3.3 consumes it · **Touches:** `ProjectSettings/TagManager.asset`
**Regression risk:** first-ever custom layers added to this project (§1.13). Global, silent change.

**Acceptance criteria:**
- [ ] Layer 8 = `Player`, Layer 9 = `Ground` added, no other layers/tags touched
- [ ] Default collision matrix left unchanged (no layers excluded from colliding)
- [ ] Regression check: diff of `TagManager.asset` shows only the two new layer name additions

#### Task 3.2: [GAMEPLAY] Player asmdef scaffold + horizontal movement
**Depends on:** Task 3.1 · **Parallel:** yes — with Task 3.5 (contract-based, see §1.7) · **Touches:** none (new system) — creates the 3 asmdefs from §1.17 and `PlayerController.cs` (scaffold + horizontal movement only)

**Acceptance criteria:**
- [ ] 3 asmdefs created per §1.17 with correct cross-references (test asmdefs reference the runtime asmdef + Unity's test-framework assemblies)
- [ ] Left/Right arrow keys move the character horizontally at 4.5 units/sec, input read in `Update`, velocity applied in `FixedUpdate`
- [ ] Horizontal velocity write preserves the existing `.y` component — verified by a test that sets a nonzero `rb.velocity.y`, applies one frame of horizontal input, and asserts `.y` is unchanged
- [ ] `flipX` toggles on last-nonzero horizontal input direction
- [ ] Sets `IsWalking` (true while grounded, horizontal input nonzero, and not committed to Attack/Defend — the "not committed" clause is the hook Task 3.4a wires into)
- [ ] Compiles and runs with no Animator/Controller attached yet (parameter-setting calls no-op safely)

#### Task 3.3: [GAMEPLAY] Jump physics, grounded check, jump Animator params, interrupt safety, EditMode tests
**Depends on:** Task 3.2 · **Parallel:** no · **Touches:** none (new system) — extends `PlayerController.cs`

**Acceptance criteria:**
- [ ] Grounded check via `OverlapCircle`/raycast against the `Ground` layer, performed in `FixedUpdate`
- [ ] Space press while grounded and not `_jumpInProgress` immediately fires `JumpTrigger` and starts a 0.08s timer; `_jumpInProgress` set true at this moment
- [ ] Jump re-trigger guard: Space presses ignored whenever `_jumpInProgress` is true, independent of `IsGrounded`
- [ ] 0.08s anticipation delay: vertical impulse (`v0 = 15 u/s`) applied exactly 0.08s after `JumpTrigger` fires, not on the initial press; horizontal movement is not frozen during this window
- [ ] Delayed-impulse cancellation: the impulse callback checks `IsDead` and whether `JumpAnticipation` is still the active Animator state at the moment it fires; no-ops if either fails
- [ ] `_jumpInProgress`'s *normal* clear condition remains "landing" (`IsGrounded` false→true) for the ordinary jump-completes-uninterrupted path; the interrupt-path clear is owned by Task 3.4a's `Hurt()`/`Die()` reset (§1.9) — this task's own logic does not attempt to clear it on interrupt, avoiding duplicated/conflicting reset logic across two methods
- [ ] Vertical impulse write preserves the existing `.x` component — same test style as Task 3.2's `.y`-preservation test
- [ ] Space + held Left/Right applies 4.5 u/s horizontal velocity simultaneously with the vertical jump impulse once it lands
- [ ] Fall velocity clamped at -20 u/s
- [ ] Sets `IsGrounded` and `VerticalVelocity` Animator parameters each `FixedUpdate`
- [ ] EditMode test: pure jump-physics math (v0/gravity derivation from apex height + time-to-apex) asserted against §1.5's numbers within a small tolerance
- [ ] EditMode test: simulated double Space-press within the 0.08s anticipation window results in exactly one `JumpTrigger` fire and one vertical impulse, not two
- [ ] EditMode test: trigger jump, then call `Die()` before the 0.08s timer elapses; assert no vertical impulse is applied

#### Task 3.4a: [GAMEPLAY] Combat/reaction layer — gating logic, full-commit gating, guard-flag resets, Animation Event wiring
**Depends on:** Task 3.3, **Task 2.3** (this task adds an Animation Event to the `Quirrel_Attack` clip, which must already exist)
**Parallel:** no · **Touches:** the `Quirrel_Attack` AnimationClip (adds an Animation Event; the clip's frames/timing/loop settings from Task 2.3 are not otherwise altered)
**Regression risk:** modifying a clip created by a different discipline's task — Task 2.3 (ART) is the clip's owner; the Animation Event addition here must not change any of Task 2.3's already-accepted timing/frame ACs

**Acceptance criteria:**
- [ ] Z press (`GetKeyDown`, edge) fires `AttackTrigger`, gated off while `DefendHeld`, `IsDead`, `!IsGrounded`, or `_isAttacking`
- [ ] `_isAttacking` set true when `AttackTrigger` fires, cleared via an Animation Event added to `Quirrel_Attack`'s final frame (on the normal completion path only)
- [ ] X held sets `DefendHeld` true, gated off while `IsDead` or `!IsGrounded`
- [ ] Full commit: while `_isAttacking` or `DefendHeld` is true, both Jump/Space AND Left/Right horizontal input are ignored
- [ ] Public `Hurt()`: fires `HurtTrigger` unless `IsDead`; starts/restarts a 0.3s hit-stun timer during which movement/attack/defend/jump input is ignored; re-entrant calls during an active window restart the timer rather than being ignored or queued
- [ ] `Hurt()` explicitly resets `_jumpInProgress = false` and `_isAttacking = false` at the moment it accepts the interrupt — unconditional, runs regardless of whether a jump or attack was actually in progress; also explicitly sets `DefendHeld = false` for immediate visual consistency
- [ ] `HurtRecoveryTrigger` fires once per completed stun episode when the timer finally elapses uninterrupted, restoring normal input handling
- [ ] Public `Die()`: fires `DieTrigger`, sets `IsDead` true permanently; all movement/jump/attack/defend/hurt input ignored thereafter
- [ ] `Die()` applies the same explicit resets as `Hurt()` (`_jumpInProgress`, `_isAttacking`, `DefendHeld` all forced false)
- [ ] Both `Hurt()`/`Die()` callable from outside the class (public method and/or C# event)
- [ ] **Regression check:** `Quirrel_Attack`'s frame content, fps, Loop Time setting, and total clip length are unchanged from Task 2.3's accepted state after the Animation Event is added

#### Task 3.4b: [GAMEPLAY] Combat/reaction layer — EditMode/PlayMode test suite for Task 3.4a
**Depends on:** Task 3.4a · **Parallel:** no · **Touches:** none (new system) — test files only

**Acceptance criteria:**
- [ ] EditMode tests: `Die()` called twice doesn't double-fire or throw; `Hurt()` after `Die()` is a no-op; `Hurt()` called a second time mid-stun restarts the 0.3s window (verified by asserting input is still locked at a time point past the *original* window's end but within the *restarted* one); `HurtRecoveryTrigger` fires exactly once per stun episode even when `Hurt()` was called multiple times within it
- [ ] EditMode/PlayMode test: with `_isAttacking` (or `DefendHeld`) true and sustained Right-arrow input held, the character's X position does not change and never becomes ungrounded — walking off the ground's edge is impossible while committed to Attack or Defend
- [ ] EditMode test: trigger a jump; call `Hurt()` before the 0.08s anticipation timer elapses (impulse cancelled, character never leaves ground); wait out the 0.3s stun and let it recover via `HurtRecoveryTrigger`; then press Space again and assert a **new** jump successfully triggers. (Distinct from the Die-during-anticipation test in Task 3.3, which cannot catch this: `IsDead` independently blocks all future input regardless of `_jumpInProgress`'s value, so it structurally can't detect a stuck flag.)
- [ ] EditMode test: trigger an attack; call `Hurt()` mid-swing before the Animation Event's frame is reached; wait out stun/recovery; then press Z again and assert a new `AttackTrigger` fires

#### Task 3.5: [GAMEPLAY] AnimatorController topology (parameters, states, transitions)
**Depends on:** none — states/parameters/transitions don't require AnimationClip assets to exist, only the final per-state Motion assignment does
**Parallel:** yes — with Phase 2 and Tasks 3.2–3.4b (all coordinate only via the fixed §1.7 contract, no shared files) · **Touches:** none (new system)

**Acceptance criteria:**
- [ ] All 10 parameters from §1.7 created with exact names and types, including `HurtRecoveryTrigger`
- [ ] All states created (Idle, Walk, JumpAnticipation, JumpRise, JumpFall, Attack, DefendRaise, DefendHold, Hurt, Die) with placeholder/empty Motion fields — real clips assigned in Task 3.6
- [ ] Full transition topology wired per §1.7, including: `Can Transition To Self = false` explicitly set on the Attack Any-State transition; the two-branch Hurt exit (`HurtRecoveryTrigger` + `IsGrounded` → Locomotion or JumpFall); Die as terminal with no outgoing transitions
- [ ] Attack and Defend Any-State entries have no redundant `IsGrounded` condition, since §1.8's code-side full-commit gate genuinely guarantees the character cannot leave the ground while in either state

#### Task 3.6: [GAMEPLAY] AnimatorController motion assignment
**Depends on:** Task 3.5, Phase 2 (Tasks 2.1–2.3) · **Parallel:** no · **Touches:** the AnimatorController asset from Task 3.5

**Acceptance criteria:**
- [ ] All 10 clips from §1.6 assigned to their matching state's Motion field, by exact name cross-check against the §1.6 table
- [ ] Controller preview: scrubbing through each state in the Animator window shows the correct sprite(s), no missing-Motion warnings

#### Task 3.7: [GAMEPLAY] CameraFollow script
**Depends on:** none · **Parallel:** yes — with everything in Phase 3 · **Touches:** none (new system)

**Acceptance criteria:**
- [ ] X-axis `SmoothDamp` toward target, `smoothTime = 0.15s`; Y and Z fixed
- [ ] Public `target` field (Transform), no hardcoded scene reference
- [ ] EditMode test: after N fixed-step calls with a stationary target, camera X converges to within 0.01 units of target X

#### Task 3.8: [GAMEPLAY] Scene assembly — Ground, Player, Camera wiring in SampleScene
**Depends on:** Tasks 3.1, 3.4b, 3.6, 3.7 · **Parallel:** no — the only task touching `SampleScene.unity`; nothing else in this plan may run concurrently with it
**Touches:** `Assets/Scenes/SampleScene.unity` (currently the untouched Unity default scene)
**Regression risk:** first meaningful content added to this scene — low risk since the scene currently has no gameplay content to break, but this scene becomes the demo/test scene other future work will build on

**Acceptance criteria:**
- [ ] `Ground` GameObject: `BoxCollider2D` on layer `Ground`, 30 units wide, top surface at world y=0, placeholder-tinted sprite (explicitly not final art)
- [ ] `Player` prefab: `SpriteRenderer` (Idle sprite default), `Rigidbody2D` (Dynamic, gravityScale ≈3.823, layer `Player`), `BoxCollider2D` sized to the Idle frame, `Animator` with the Task 3.6 controller, `PlayerController` component, spawned at (0,0,0)
- [ ] Player collider's bottom edge sits at local y=0 with zero gap and zero clipping against Ground's top surface at world y=0 — checked via bounds inspection in the Inspector
- [ ] `Main Camera`: orthographic size 5, `CameraFollow` targeting the Player
- [ ] Regression check: `SampleScene.unity` opens without console errors/warnings; no missing script/reference warnings on any of the three new GameObjects
- [ ] Play Mode manual smoke check: idle, walk both directions, jump (vertical-only and diagonal), attack once per Z press, defend hold/release, `Hurt()`/`Die()` calls via Inspector/test menu all visibly play their animations, and Hurt visibly returns to Locomotion/JumpFall afterward rather than freezing
- [ ] Manual check: attack or hold Defend while holding Right-arrow near the ground's edge — character does not move and does not fall off
- [ ] Manual check: interrupt a jump-in-progress and an attack-in-progress with `Hurt()` (separately), let each recover, and confirm jump/attack both still work afterward

---

**Phase 3 dependency graph:**
```
3.1 [GAMEPLAY] layers ──→ 3.2 [GAMEPLAY] scaffold+horizontal ──→ 3.3 [GAMEPLAY] jump physics ──┐
                                                                                                 │
2.3 [ART] Attack/Defend/Hurt/Die clips ────────────────────────────────────────────────────────┼──→ 3.4a [GAMEPLAY] combat gating/resets ──→ 3.4b [GAMEPLAY] combat test suite ──┐
                                                                                                 │                                                                              │
3.5 [GAMEPLAY] Animator topology (independent, parallel with 3.2–3.4b and Phase 2) ──→ 3.6 [GAMEPLAY] motion assignment ──────────────────────────────────────────────────────┤
                                                              ↑                                                                                                                │
Phase 2 (2.1/2.2/2.3) ────────────────────────────────────────┘                                                                                                                │
                                                                                                                                                                                │
3.7 [GAMEPLAY] CameraFollow (independent, parallel with all) ──────────────────────────────────────────────────────────────────────────────────────────────────────────────────┼──→ 3.8 [GAMEPLAY] scene assembly
                                                                                                                                                                                ┘
```

---

## Phase 4 — QA [QA]

#### Task 4.1: [QA] EditMode regression suite
**Depends on:** Tasks 3.4b, 3.7 · **Parallel:** yes — with Task 3.6/3.8 work in progress on non-overlapping files · **Touches:** none (new system)

**Acceptance criteria:**
- [ ] Verifies and extends the EditMode tests already committed in Tasks 3.2, 3.3, 3.4b, and 3.7 (jump math, re-trigger guard, delayed-impulse cancellation on Hurt/Die, the two interrupt-then-recover-then-retry tests from 3.4b, hit-stun restart/episode semantics, Attack no-stack guard, full-commit gating, CameraFollow convergence) — does not re-author them from scratch
- [ ] Animator parameter contract test: asserts the 10 string constants used by `PlayerController` match the 10 parameters actually present in the Task 3.6 AnimatorController asset — fails loudly on a typo/desync
- [ ] Extended: the same style of contract test also asserts any Animator *state names* referenced by code (e.g. an `IsName("JumpAnticipation")`-style check used by the delayed-impulse cancellation guard in Task 3.3) actually exist as states in the Task 3.6 controller
- [ ] All tests pass in the Unity Test Runner (EditMode)

#### Task 4.2: [QA] PlayMode tests + live Play-mode verification via Unity MCP bridge
**Depends on:** Task 3.8 · **Parallel:** no — needs the fully wired scene · **Touches:** `Assets/Scenes/SampleScene.unity` (read during test execution, not modified)

**Acceptance criteria:**
- [ ] PlayMode test: simulated Right-arrow hold for 1s moves the Player ≈4.5 units on X (within tolerance)
- [ ] PlayMode test: simulated Space press measures apex time within 0.4s ± 0.05s and apex height within 3.0 units ± 0.15, with the vertical impulse verifiably delayed ≈0.08s from the initial press
- [ ] PlayMode test: Space + Right produces both vertical and horizontal displacement simultaneously
- [ ] PlayMode test: Z press fires exactly one Attack per press; rapid repeated presses during an active swing do not queue/stack extra plays
- [ ] PlayMode test: X held enters and stays in Defend; release returns to Locomotion
- [ ] PlayMode test: `Hurt()` call transitions to Hurt and, after the stun window, returns to Locomotion or JumpFall rather than freezing
- [ ] PlayMode test: jump, then call `Die()` mid-anticipation; assert no upward launch occurs
- [ ] PlayMode test: hold Right-arrow while Attack/Defend is active near the ground's edge; assert the character does not move and does not become airborne
- [ ] PlayMode test: interrupt an in-progress jump with `Hurt()` mid-anticipation, let it recover, then press Space again — assert the character actually jumps (apex height/timing within the usual tolerance)
- [ ] Live verification via the Unity MCP bridge: enter Play mode, exercise all five inputs plus both external `Hurt()`/`Die()` calls, confirm via `read_console` that no errors/warnings/missing-reference exceptions occur during any transition, and confirm Die correctly locks out all further input
- [ ] Regression check: re-running the full existing test suite (Task 4.1 + this task) after Task 3.8's scene wiring shows no new failures introduced by scene assembly

#### Task 4.3: [QA] Manual playtest protocol document
**Depends on:** Task 4.2 · **Parallel:** no · **Touches:** none (new system)

**Acceptance criteria:**
- [ ] Written checklist (not prose) covering feel-only items automated tests can't assert: does the jump read as "snappy," does the attack read as "punchy," does Defend read as a committed block stance, does Die read as appropriately weighty per the uneven keyframe timing in §1.6
- [ ] Includes explicit steps to test the diagonal jump (Space+Left and Space+Right), the "walk off the ground plane's edge" boundary case (including attack/defend-near-edge specifically), and interrupting a jump/attack with a hurt event mid-action, confirming the next jump/attack still works
- [ ] Document lives alongside the plan file location convention (e.g. `Docs/Plans/002_.../manual-playtest.md` or equivalent, decided at implementation time)

---

**Phase 4 dependency graph:**
```
3.4b, 3.7 ──→ 4.1 [QA] EditMode suite ─┐
                                          ├──→ 4.3 [QA] manual playtest protocol
3.8 ────────→ 4.2 [QA] PlayMode + live MCP verification ─┘
                                                           └──→ (Phase 5)
```

---

## Phase 5 — Build Verification [BUILD]

#### Task 5.1: [BUILD] Windows Standalone build-and-run smoke check, Linux compile check
**Depends on:** Task 4.2 · **Parallel:** no — final gate on the whole feature · **Touches:** none (new system) — build output only, no source changes expected
**Regression risk:** first player build this project has ever produced; surfaces any latent Editor-only assumption for the first time

**Acceptance criteria:**
- [ ] Windows Standalone (x86_64) build completes with zero errors and zero new warnings versus the Editor console baseline
- [ ] Built `.exe` launched and manually smoke-tested: `SampleScene` loads, character is visible and controllable (walk both directions, jump, attack, defend), matching the Task 3.8 Play Mode smoke check
- [ ] Linux build (via the installed `com.unity.toolchain.win-x86_64-linux-x86_64`) completes with zero errors — compile/build verification only, no runtime smoke test (no Linux execution environment available to this pipeline; stated explicitly as an out-of-scope boundary, not silently skipped)
- [ ] Any Editor-only API usage or platform-conditional code surfaced by either build is filed as a follow-up, not silently patched around under this task

---

## 4. Full Task List / Discipline Summary

| # | Task | Discipline | Hours (est.) |
|---|---|---|---|
| 1.1 | Correct ART.md frame table | ART | 0.5–1 |
| 1.2 | Build slicer/knockout tool | GAMEPLAY | 3–4 |
| 1.3 | Run tool, accept/reject 24 frames | ART | 2–3 |
| 1.4 | Sprite import configuration | ART | 1–2 |
| 2.1 | Idle/Walk clips | ART | 1–2 |
| 2.2 | Jump 3-phase clips | ART | 1–2 |
| 2.3 | Attack/Defend/Hurt/Die clips | ART | 2–3 |
| 3.1 | Physics layers | GAMEPLAY | 0.5–1 |
| 3.2 | Asmdef scaffold + horizontal movement | GAMEPLAY | 3–4 |
| 3.3 | Jump physics + grounded check + interrupt safety + tests | GAMEPLAY | 3.5–4 |
| 3.4a | Combat gating logic, full-commit gating, guard resets, Animation Event | GAMEPLAY | 2.5–3 |
| 3.4b | Combat EditMode/PlayMode test suite | GAMEPLAY | 2–3 |
| 3.5 | AnimatorController topology | GAMEPLAY | 2–3 |
| 3.6 | AnimatorController motion assignment | GAMEPLAY | 0.5–1 |
| 3.7 | CameraFollow | GAMEPLAY | 1 |
| 3.8 | Scene assembly | GAMEPLAY | 3 |
| 4.1 | EditMode suite | QA | 2–3 |
| 4.2 | PlayMode + live MCP verification | QA | 3–4 |
| 4.3 | Manual playtest protocol | QA | 1–2 |
| 5.1 | Build-and-run smoke check | BUILD | 1–2 |

20 tasks, all within the 1–4 hour granularity requirement.

---

## 5. Judgment Calls Made Explicit (accumulated across all review rounds)

1. Jump built as a 3-phase, physics-condition-driven Animator sub-state-machine rather than a single flipbook — only 3 source poses exist.
2. Die triggers a permanent input-lock (`IsDead`) — decided in-scope, minimal cost.
3. Background knockout uses border-connected flood fill, not a global color threshold — protects enclosed near-white regions (eyes, hood highlights).
4. Per-frame sprite pivot deviates from the sheet-level Center baseline to Bottom — justified by varying per-frame crop heights.
5. Legacy `UnityEngine.Input`, not the new Input System — package isn't installed.
6. This plan adds the project's first-ever physics layers (Player, Ground).
7. Coyote time, jump buffering, asymmetric fall gravity, Defend release animation, landing squash are explicitly deferred, not silently omitted.
8. Attack/Defend fully commit the character in place (both Jump and horizontal input gated) — closes a walk-off-the-ledge bug found in review.
9. Hurt exits via `HurtRecoveryTrigger`, branching on `IsGrounded` (can occur mid-air, unlike Attack/Defend).
10. Hurt re-entrancy restarts the stun timer; `HurtRecoveryTrigger` fires once per stun episode, not once per `Hurt()` call.
11. Linux verification is build/compile-only, not a runtime smoke test — no Linux execution environment available.
12. The delayed jump impulse is cancelled if Hurt/Die interrupts `JumpAnticipation` before the 0.08s timer elapses — prevents launching a dead character's corpse.
13. `_isAttacking` is cleared via an Animation Event on the Attack clip's normal-completion path, for tighter sync than a code timer.
14. Grounded-check is explicitly pinned to `FixedUpdate`, alongside the physics step it gates.
15. `_jumpInProgress` and `_isAttacking` are both explicitly reset inside `Hurt()`/`Die()` at interrupt-acceptance time, rather than relying on either flag's owning state to signal its own completion — fixes a stuck-forever-after-a-hit bug in both, found in review round 3. `DefendHeld` is also force-cleared on interrupt for visual tidiness (it wasn't at risk of the same bug).
16. Task 3.4a declares its dependency on Task 2.3 (the `Quirrel_Attack` clip must exist before an Animation Event can be added to it) — a gap found in review round 3.
17. The former combined Attack/Defend/Hurt/Die task was split into 3.4a (production gating/reset logic) and 3.4b (its test suite) after accumulating scope across four review passes, keeping both within the project's 1–4 hour task-granularity rule.

---

## 6. Reference file paths consulted while drafting/reviewing this plan

- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\CLAUDE.md`
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\ART.md` (§7)
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Docs\Plans\001_pin-head-recolor.md` (§1.4–1.5 — frame map and coordinate convention)
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Packages\manifest.json` (confirmed no Input System package, confirmed Linux toolchain present)
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\ProjectSettings\TagManager.asset` (confirmed zero existing tags/layers)
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\.gitattributes` (confirmed repo-wide `*.png` LFS tracking)
- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\Assets\Sprites\Quirrel_Sprites.png` (source sheet)
