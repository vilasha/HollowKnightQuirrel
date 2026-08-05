# Manual Playtest Protocol — Quirrel Player Control (Plan 002)

**Covers:** Task 4.3 of `Docs/Plans/002_quirrel-sprite-animation-player-control.md`
**Purpose:** subjective/feel judgments and physical-boundary checks that the automated
EditMode/PlayMode suites (Tasks 4.1–4.2, 30/30 green) cannot assert. Run this in the Unity
Editor, in Play Mode, on `Assets/Scenes/SampleScene.unity`.
**Tester:** any human with the Editor open. No MCP tooling is required for most items; two
items (Hurt/Die interrupt tests) require one of the workarounds in "How to trigger Hurt() and
Die()" below — read that section before starting.

**Sign-off rule:** every box must be checked, with a PASS/FAIL noted, before this feature
clears the manual playtest gate. Any FAIL is logged as a bug per the QA bug report format
(severity per the standard table — a feel regression that contradicts the plan's stated
numbers is Major minimum; a boundary/softlock issue is Critical).

---

## 0. How to trigger `Hurt()` and `Die()` for this pass

`PlayerController.Hurt()` and `PlayerController.Die()` are public, parameterless methods with
**no in-game trigger** (no combat system exists — confirmed in `PlayerController.cs`) and **no
`[ContextMenu]` attribute**, so they are **not** reachable from the plain Inspector's
right-click component menu, and the Console window cannot invoke arbitrary instance methods
without a custom editor script. Do not assume a button exists — there isn't one yet.

Two real options, in order of preference:

**Option A — Unity MCP `execute_code` (preferred, if available to you):**
While in Play Mode, run:
```csharp
GameObject.Find("Player").GetComponent<PlayerController>().Hurt();
```
or `.Die()`. This is exactly how Hurt/Die were exercised for this feature's own live
verification (Task 4.2) and is the most direct path if you have MCP access.

**Option B — temporary `[ContextMenu]` attributes (if you don't have MCP access):**
Ask @gameplay-programmer for a throwaway, reverted-after-this-pass change: add
`[ContextMenu("Debug Hurt")]` above `public void Hurt()` and `[ContextMenu("Debug Die")]`
above `public void Die()` in `Assets/Scripts/Player/PlayerController.cs`. With that in place,
select the `Player` GameObject in Play Mode and right-click the `PlayerController` component
header (or use the ⋮ menu) to invoke each method on demand. Revert the attributes before
committing — they are a testing convenience only, not part of the accepted plan surface.

Do not sign off Section 4/5 below (the Hurt-interrupt items) without actually triggering
`Hurt()` via one of these two paths — do not skip them or approximate them by other means.

---

## 1. Feel-only checks (subjective, no automated equivalent)

| # | Check | Steps | Expected | Pass/Fail |
|---|---|---|---|---|
| 1.1 | Jump snappiness | Enter Play Mode. Stand still, press Space once. Watch the full arc. | Reads as **snappy**, not floaty: a brief (0.08s) crouch beat before launch, then a fast, decisive rise to apex (~3.0 units high, ~0.4s to apex) and a matching fall. Should feel closer to Hollow Knight's jump than a slow "balloon" arc. | |
| 1.2 | Jump anticipation is visible but not sluggish | Press Space again, watch only the first ~0.1s. | The crouch (`JumpAnticipation`, F11) is clearly visible as a distinct beat before the character leaves the ground — but it should read as a quick coiled wind-up, not a pause or hesitation. | |
| 1.3 | Attack punch | Stand still, press J once. Watch the full swing. | Reads as **punchy**: fast 4-frame swing, done in a quarter-second (0.25s @ 16fps) — should feel abrupt and committed, not a slow telegraphed wind-up. | |
| 1.4 | Attack — no stacking/mushiness on rapid presses | Rapidly press J several times in a row. | Each swing completes cleanly; rapid presses do not visibly queue, stutter, or blend two swings together. (Automated coverage exists for the *trigger count*; this checks it also *looks* clean.) | |
| 1.5 | Defend — committed block, not a flinch | Hold K and watch the raise + hold pose. | Reads as a deliberate, held block stance — weight settles into it, not a nervous flinch or a quick tap-and-release look. Release K and confirm it snaps back to Locomotion immediately (no reverse-raise animation — this is correct per the plan, not a bug). | |
| 1.6 | Die — weighty, uneven collapse | Trigger `Die()` (see Section 0). Watch the full sequence. | Should visibly feel like it's **slowing down and settling**, not looping evenly: first pose (Die_01) holds briefly (0.15s), second pose (Die_02) holds noticeably longer (0.35s), then the final pose (Die_03) holds indefinitely. The pacing itself should read as weight settling to the ground, not a uniform flipbook loop. | |
| 1.7 | Die — permanent lock reads correctly | After Die settles into its final held pose, try every input (A/D, Space, J, K). | No input has any visible effect — character stays frozen in the final Die pose. No console errors. | |

---

## 2. Diagonal jump

| # | Check | Steps | Expected | Pass/Fail |
|---|---|---|---|---|
| 2.1 | Space + Left | Stand in open ground (not near an edge). Press and hold A, then press Space while still holding A. | Character launches into a jump arc while continuing to move left — both vertical and horizontal motion visibly happen together, not sequentially. `flipX` shows the character facing/moving left throughout. | |
| 2.2 | Space + Right | Same as 2.1, mirrored: hold D, press Space while holding it. | Same as 2.1, mirrored — diagonal arc to the right, facing right. | |
| 2.3 | Direction change mid-air | Jump straight up (Space alone), then while airborne press and hold A or D. | Character gains horizontal control in the air (full air control per the plan) — the horizontal direction press changes trajectory mid-flight, not just at launch. | |
| 2.4 | Anticipation beat still applies diagonally | Repeat 2.1/2.2, watching the first ~0.1s closely. | The 0.08s crouch beat is still visible before the diagonal launch — horizontal movement during that beat is not frozen (per §1.4 of the plan), so the character should already be sliding in the held direction before the vertical impulse fires. | |

---

## 3. Ground-plane edge boundary

**Plan finding (confirmed by re-reading §1.15 and §1.8, and by inspecting `PlayerController.cs`
directly — no source was modified to check this): the Ground is a single fixed 30-unit-wide
`BoxCollider2D` with no side walls and no horizontal clamp on ordinary locomotion. The
"character cannot leave the ground" guarantee in this plan is explicitly scoped to Attack/Defend
only (§1.8's full-commit gating) — it is not a general off-the-edge guard for plain walking.**
**Expected behavior for plain Walk/Jump: walking past the Ground collider's edge while not
attacking/defending is expected to let the character walk off into empty space and fall — this
is not a bug, it is out of scope per plan §2 ("Level design / tilemap / real ground collision
beyond the single minimal ground plane" is explicitly listed as out of scope). Do not fail this
item unless the behavior differs from that.**

| # | Check | Steps | Expected | Pass/Fail |
|---|---|---|---|---|
| 3.1 | Plain walk off the left edge | Hold A continuously from spawn until well past the Ground sprite's visible left edge. | Character walks past the visible edge of the Ground and falls (loses ground contact, gravity takes over) once no longer overlapping the `BoxCollider2D`. This is expected per the plan, not a regression — confirm it happens smoothly (no snagging, no console errors) rather than confirm it doesn't happen. | |
| 3.2 | Plain walk off the right edge | Same as 3.1, mirrored with D. | Same as 3.1, mirrored. | |
| 3.3 | Attack near the edge — full commit | Walk to just short of the Ground's edge (either side). Press J to start an attack, and while the swing is playing, press and hold the direction toward the edge. | Character does **not** move at all during the swing — X position is unchanged for the full 0.25s attack, regardless of held directional input. Character remains grounded throughout. | |
| 3.4 | Defend near the edge — full commit | Walk to just short of the Ground's edge. Hold K to enter Defend, and while held, press and hold the direction toward the edge. | Character does **not** move at all while `DefendHeld` is true — X position is unchanged, character remains grounded. Release K only after confirming no drift occurred. | |

---

## 4. Jump interrupted by Hurt() mid-anticipation

**Regression context:** a real bug was found in this plan's review (round 3) where an
interrupted jump could leave `_jumpInProgress` stuck `true` forever, silently disabling all
future jumps for the rest of the session. Automated tests cover this; this manual pass is the
second line of defense.

| # | Check | Steps | Expected | Pass/Fail |
|---|---|---|---|---|
| 4.1 | No launch on interrupt | Press Space to start a jump. **Within the first 0.08s** (immediately — do not wait), trigger `Hurt()` via Section 0's method. | The character does **not** launch upward — no vertical impulse is applied. The Hurt pose plays; the character stays on the ground (or at whatever height it was at when Hurt landed) instead of being flung into the air mid-Hurt. | |
| 4.2 | Recovery timing | After triggering Hurt() as in 4.1, wait out the full 0.3s hit-stun window without pressing anything. | After ~0.3s, the character returns to Locomotion (Idle/Walk) automatically — does not freeze in the Hurt pose. | |
| 4.3 | Jump works again after recovery | Immediately after 4.2's recovery, press Space. | A **brand-new jump** triggers successfully — full arc, crouch beat, apex, fall, all as in Section 1.1. This confirms the Hurt interrupt did not silently disable jumping for the rest of the session. | |

---

## 5. Attack interrupted by Hurt() mid-swing

**Regression context:** the same stuck-flag bug class as Section 4, but for `_isAttacking` —
if Hurt/Die interrupts an attack before its final frame, the Animation Event that normally
clears `_isAttacking` never fires, and without the explicit reset in `Hurt()`/`Die()`, Attack
would be permanently disabled after any hit landed mid-swing.

| # | Check | Steps | Expected | Pass/Fail |
|---|---|---|---|---|
| 5.1 | Interrupt mid-swing | Press J to start an attack. **Before the swing finishes** (within the first ~0.15–0.2s of the 0.25s clip — don't wait for it to complete), trigger `Hurt()` via Section 0's method. | The attack pose is cut short and the Hurt pose plays instead — no visible attempt to finish the swing after Hurt starts. | |
| 5.2 | Recovery | Wait out the 0.3s hit-stun window without pressing anything. | Character returns to Locomotion automatically, same as 4.2. | |
| 5.3 | Attack works again after recovery | Immediately after 5.2's recovery, press J. | A **brand-new attack** fires successfully — full 4-frame swing plays to completion. Confirms the Hurt interrupt did not silently disable Attack for the rest of the session. | |
| 5.4 | Rapid re-press sanity check | After 5.3's attack completes naturally, immediately press J several more times in quick succession. | Each subsequent attack fires cleanly, one per press, same as Section 1.4 — confirms the guard flag is in a clean, non-stuck state going forward, not just for the single retry in 5.3. | |

---

## 5a. Attack while jumping (`Docs/Plans/005_attack-while-jumping.md`)

**Context:** pressing J while airborne now starts an air-attack that keeps the jump's real
physics trajectory fully live (no freeze of horizontal or vertical velocity) — a ground-attack
still freezes the character in place exactly as before. This section is the manual/feel gate
for that split; automated coverage lives in `PlayerControllerTests.cs`,
`AnimatorContractTests.cs`, and `PlayerControllerPlayModeTests.cs`.

| # | Check | Steps | Expected | Pass/Fail |
|---|---|---|---|---|
| 5a.1 | Jump + attack mid-air keeps trajectory | Press Space to jump. While airborne (anywhere in the rise or fall), press J. | The character keeps moving/falling exactly as it would without the attack — no visible pause, stutter, or freeze in the jump arc while the Attack pose plays. Held A/D during the swing still steers the arc (air control is not frozen either). | |
| 5a.2 | Attack at jump apex resumes into falling pose, not idle/walk | Time a J press to land as close to the jump's apex as you can (~0.4s after the vertical impulse, Section 1.1's timing). | Once the Attack swing finishes, the character's pose resumes as a falling pose (or a rising pose if the swing finished slightly before the apex) — never snaps to Idle or Walk while still airborne. | |
| 5a.3 | Ground-attack still visibly freezes (regression spot-check) | Stand still on the ground, press J. | Character does **not** move at all during the swing, exactly as before this feature — regression check against Section 3.3 above, confirming ground-attacks are unaffected by the air-attack change. | |

---

## 5b. Look up/down camera pan (Docs/Plans/006_look-up-down-camera-pan.md)

**Context:** holding `W` pans the camera up by a plausible half-screen amount (driven by
`Camera.orthographicSize`); holding `S` pans it down; releasing either returns smoothly to the
normal position. Pan is gated on `PlayerController.IsDead || PlayerController.IsFullyCommitted`
— **not** `IsFullyCommitted` alone, since `Die()` clears every flag `IsFullyCommitted` reads, so
the post-`Die()` row below is the one case where `IsFullyCommitted` itself is `false` and only
`IsDead` is doing the blocking. This section is the manual/feel gate for that gating; automated
coverage lives in `CameraFollowTests.cs`.

| # | Check | Steps | Expected | Pass/Fail |
|---|---|---|---|---|
| 5b.1 | W pans up smoothly | Stand still on the ground. Press and hold W. | Camera slides smoothly upward, converging to roughly 40% of a screen height above normal — no snap, no overshoot/jitter. | |
| 5b.2 | Release W returns to normal | Release W after 5b.1 has converged. | Camera slides smoothly back down to its normal position over a similar time constant — no snap. | |
| 5b.3 | S pans down, release returns to normal | Press and hold S; once converged, release it. | Camera slides smoothly downward by roughly 40% of a screen height, then smoothly returns to normal on release — mirrors 5b.1/5b.2. | |
| 5b.4 | Both W and S held simultaneously produce no pan | Press and hold W, then also press and hold S (both held together), and watch the camera. | Camera stays at its normal position the entire time both keys are held — no pan in either direction. This is a regression pin for the accumulation-cancel design (`+1`/`-1` netting to `0`), the same shape as A/D. | |
| 5b.5 | Pan fully blocked during an Attack | Press J to start an attack. While the swing is playing, press and hold W (or S). | Camera does not pan at all during the swing — stays at its normal position regardless of held W/S. | |
| 5b.6 | Pan fully blocked during a Defend hold | Hold K to enter Defend. While `DefendHeld` is true, press and hold W (or S). | Camera does not pan at all while Defend is held — stays at its normal position. | |
| 5b.7 | Pan fully blocked during a Hurt stun window | Trigger `Hurt()` via Section 0's method. During the 0.3s hit-stun window, press and hold W (or S). | Camera does not pan at all during the stun window — stays at its normal position. | |
| 5b.8 | Pan fully blocked after `Die()` specifically | Trigger `Die()` via Section 0's method. After Die settles into its final held pose (`IsFullyCommitted` is `false` at this point — Die clears every flag it reads — only `IsDead` is doing the blocking here), press and hold W (or S). | Camera does not pan at all — stays at its normal position. This is a **separate, distinct check from 5b.5–5b.7**: it specifically confirms the `IsDead` branch of the gate is independently load-bearing, not merely redundant with the `IsFullyCommitted` branch. | |
| 5b.9 | Mid-pan full-commit/death glides back, doesn't snap | Press and hold W (or S) until the camera has visibly panned partway (don't wait for full convergence). While still mid-pan, trigger one of: an Attack (J), a Defend hold (K), `Hurt()`, or `Die()` (via Section 0). | Camera glides smoothly back to its normal position over the same time constant as 5b.2/5b.3 — no instant snap-cut, regardless of which full-commit/death trigger interrupted the pan. | |

---

## 5c. Look up/down idle animation (Docs/Plans/007_look-up-down-idle-animation-and-pan-tuning.md)

**Context:** holding `W` while standing still and grounded shows a looking-up pose overlaid on
`Idle`; holding `S` shows a looking-down pose. This is a **cosmetic, idle-only** overlay, gated
narrowly to idle AND grounded — it cancels immediately on walk, jump, attack, defend, hurt, or
die, and is a code-side gate independent of `CameraFollow`'s own W/S read (Section 1, Decision 3
of the plan). Automated coverage lives in `PlayerControllerTests.cs` and
`AnimatorContractTests.cs`. This section is the manual/feel gate for the animation itself, plus a
regression check that the camera pan (Section 5b) was not accidentally narrowed by this change.

| # | Check | Steps | Expected | Pass/Fail |
|---|---|---|---|---|
| 5c.1 | Looking-up pose shows while idle | Stand still on the ground. Press and hold W. | The looking-up pose displays immediately and holds while W is held. | |
| 5c.2 | Looking-down pose shows while idle | Stand still on the ground. Press and hold S. | The looking-down pose displays immediately and holds while S is held. | |
| 5c.3 | Releasing either key returns cleanly to Idle | Release W (or S) after its look pose is showing. | Character returns cleanly to the normal `Idle` pose/loop — no stuck frame, no visible pop. | |
| 5c.4 | Both W and S held simultaneously shows neither look pose | Stand still. Press and hold W, then also press and hold S (both held together). | Neither look pose displays — character stays in normal `Idle` the entire time both keys are held. This is a regression pin for the accumulation-cancel design (`+1`/`-1` netting to `0`) — the animation's **own independent** implementation of this cancel, distinct from `5b.4`'s check of the same cancel behavior on the camera side. Both must hold independently; this row is not redundant with 5b.4. | |
| 5c.5 | Starting to walk cancels a look pose into Walk | While a look pose is showing (W or S held), press and hold A or D. | The look pose cancels immediately and the character transitions straight into `Walk` — no lingering look-pose frame. | |
| 5c.6 | Jumping cancels a look pose into the jump sequence | While a look pose is showing, press Space. | The look pose cancels immediately and the jump sequence (anticipation → rise) plays normally — no lingering look-pose frame, even briefly. | |
| 5c.7 | Attacking cancels a look pose | While a look pose is showing, press J. | The look pose cancels immediately and the Attack swing plays normally. | |
| 5c.8 | Defending cancels a look pose | While a look pose is showing, press and hold K. | The look pose cancels immediately and the Defend raise/hold plays normally. | |
| 5c.9 | `Hurt()` cancels a look pose | While a look pose is showing, trigger `Hurt()` via Section 0's method. | The look pose cancels immediately and the Hurt pose plays — no lingering look-pose frame, matching the `DefendHeld` force-clear precedent. | |
| 5c.10 | `Die()` cancels a look pose | While a look pose is showing, trigger `Die()` via Section 0's method. | The look pose cancels immediately and the Die sequence plays — no lingering look-pose frame. | |
| 5c.11 | Quick W→S (or S→W) swap passes through Idle imperceptibly | While holding W (look-up showing), quickly release W and press S (or the reverse). | This is a known, accepted design simplification (Decision 8): there is no direct `LookUp`↔`LookDown` transition, so the swap passes through one frame of `Idle` before the new look pose appears. Confirm this reads as **imperceptible** — a clean, instant-feeling swap, not a visible flicker/glitch. Escalate as a bug if it reads as a visible glitch rather than an instant swap. | |
| 5c.12 | Camera pan (Section 5b) is not newly restricted by this feature | Walk, jump, attack, and defend in turn, pressing and holding W (or S) during each. | The camera still pans (or is still blocked per Section 5b's existing gating) exactly as it did before this plan — W/S continues to pan the camera during `Walk`/`Jump`/etc. exactly as before, even though the look-pose animation itself no longer displays in those non-idle states. This confirms the animation's narrower idle+grounded gate is fully independent of, and does not leak into, the camera's own separate gating. | |

---

## 5d. Bench sit mechanic (Docs/Plans/008_bench-sit-mechanic.md)

**Context:** standing anywhere along the bench's horizontal footprint (any X-axis overlap with
the bench's trigger collider) and pressing `W` while idle and grounded sits Quirrel
down; the game starts with him already seated (the bench is colocated with the spawn point).
**Correction (2026-08-05, per the user, overriding plan 008 Decision 3):** the original plan
008 requirement that sitting needs "no centering" was wrong — sitting should center Quirrel on
the bench. See Bug 3 below and row 5d.2, which is superseded by this correction and needs a
follow-up implementation.
While seated, Attack (`J`)/Jump (`Space`)/Defend (`K`) are fully blocked at the code level;
walking (`A`/`D`) is the one stated exit and stands him up immediately. Sitting is deliberately
**not** folded into `IsFullyCommitted` (Decision 2) — `CameraFollow`'s own W/S pan gate (Section
5b) is untouched by design and should still pan normally while seated; this is a named,
non-obvious judgment call, not an oversight. Automated coverage lives in
`PlayerControllerTests.cs`, `AnimatorContractTests.cs`, and `BenchTests.cs`. This section is the
manual/feel gate for the mechanic itself, the Decision 10 spawn-timing edge case, and the
Decision 2 camera-pan choice.

| # | Check | Steps | Expected | Pass/Fail |
|---|---|---|---|---|
| 5d.1 | Game starts already seated (Decision 10) | Enter Play Mode fresh — do not press any key before observing. Look at Quirrel at spawn. | Quirrel is already in the `Sitting` pose the instant Play Mode starts — no `W` press needed. **Named edge case (Decision 10):** if he spawns standing instead, this is the accepted frame-0 timing risk, not automatically a bug — press `W` once and confirm that single press then correctly sits him; note which behavior was actually observed. | PASS (code/state) — verified live via Unity MCP: `IsSitting`/`IsNearBench` both true and the Animator is in `Sitting` the instant Play Mode starts, no frame-0 edge case observed. **Visually degraded by Bug 3 below** — see Bugs Found. |
| 5d.2 | ~~Sitting from anywhere along the bench's footprint, not centered~~ **SUPERSEDED — see Bug 3** | Stand at the near edge of the bench's footprint (not centered under it) and press `W`. Repeat standing at the far edge of the footprint. | ~~Both positions sit Quirrel down on `W` — no need to be centered on the bench.~~ **Corrected requirement (per the user, overriding plan 008 Decision 3): sitting should center Quirrel on the bench, not leave him wherever he was standing.** This row's original expectation is wrong and needs to be rewritten once the centering fix lands. | FAIL against the corrected requirement — `TrySit` does succeed from both edges (mechanically works), but Quirrel is left off-center rather than snapped to the bench, which is now the wrong behavior. See Bug 3. |
| 5d.3 | `W` away from the bench still shows the ordinary look-up pose (regression vs. plan 007) | Walk well clear of the bench's footprint, stand idle, press and hold `W`. | The ordinary look-up pose (Section 5c) displays exactly as it did before this feature — `IsNearBench` is false here, so there is no interaction with sitting. | PASS — verified live at `x=5` (well clear of the bench): `IsNearBench=false`, `LookingUp=true`, `IsSitting=false`. |
| 5d.4 | `A`/`D` while seated stands Quirrel up and he walks immediately | While seated, press and hold `A` (repeat separately with `D`). | Quirrel stands and walks in that direction on the same press — no lingering seated frame, no pause between standing and walking starting. | PASS — verified live: `IsSitting` clears and the Animator transitions `Sitting → Walk` in the same frame's input, movement is not frozen (`velocity.x` reaches 4.5 immediately). |
| 5d.5 | Attack (`J`) does nothing while seated | While seated, press `J`. | No attack swing plays; character remains seated; no console errors. | PASS — verified live: `TryAttack` returns `false` and `IsAttacking` stays `false` while seated; no console errors. |
| 5d.6 | Jump (`Space`) does nothing while seated | While seated, press `Space`. | No jump anticipation or launch plays; character remains seated and grounded. | PASS — verified live: `TryJump` returns `false`, `IsJumpInProgress` stays `false` while seated. |
| 5d.7 | Defend (`K`) does nothing while seated | While seated, press and hold `K`. | No defend raise/hold pose plays; character remains seated for the entire hold. | PASS — verified live: `DefendHeld`'s computation reads `false` while seated even with K's contribution folded in as held. |
| 5d.8 | `Hurt()` interrupts sitting immediately | While seated, trigger `Hurt()` via Section 0's method. | The seated pose cuts immediately to the Hurt pose — no lingering seated frame, matching the existing `DefendHeld`/`LookingUp` force-clear precedent (Decision 6). | PASS — verified live: `IsSitting` clears immediately, Animator moves to `Hurt` via the Any-State transition, `IsSitting` Animator bool is `false`. |
| 5d.9 | `Die()` interrupts sitting immediately | While seated, trigger `Die()` via Section 0's method. | The seated pose cuts immediately into the Die sequence — no lingering seated frame. | PASS — verified live: `IsSitting` clears immediately, Animator moves to `Die` via the Any-State transition. |
| 5d.10 | Walking across the bench's footprint produces no snagging/blocking | Approach from one side and hold `A`/`D` continuously through and past the bench's full horizontal footprint without stopping. | Character walks straight through the bench's footprint at a constant, unbroken pace — no snag, no bump, no velocity change (regression check confirming Task 5.2's collider stayed a trigger, never a solid collider). | PASS — verified live via `Rigidbody2D.GetContacts`: solid-contact count stayed constant (2, both attributable to the Ground collider) whether the player was inside or outside the bench's AABB — the bench's trigger collider contributes zero physical contacts. |
| 5d.11 | Camera `W`/`S` pan (Section 5b) still works normally while seated (Decision 2) | While seated, press and hold `W` (repeat separately with `S`). | Camera pans smoothly exactly as Section 5b describes — **not** blocked by sitting. Confirms this deliberate, non-obvious Decision 2 choice reads correctly in practice, not as a bug. | PASS — confirmed by the user via real keyboard input in Play Mode: camera pans up/down normally with W/S while Quirrel is seated. |

**Tester:** live Unity MCP session (rows 5d.1–5d.10) + Maria via real keyboard input (row 5d.11), 2026-08-05.

**Bugs found during this pass** (see "Bugs Found" section below — not fixed here, routed back through the pipeline per CLAUDE.md):
1. `Bench_01.png` has visible white background leftovers on the backrest that the knockout pass should have removed.
2. The `Bench` GameObject is positioned too high — it reads as floating above the ground rather than resting on it.
3. Because sitting doesn't move Quirrel's transform (by design — Decision 3/"no centering required") but `Quirrel_Sitting_01.png` has partial bench geometry baked into the sprite itself (an accepted trade-off from plan 008 Task 1.2), the two don't visually line up: the real `Bench_01` prop and the partial bench baked into Quirrel's own sprite render as two disconnected bench fragments instead of one coherent bench.

---

## Bugs Found — 5d bench sit mechanic (2026-08-05)

### Bug: Bench sprite has residual white background around the backrest

**Severity:** Minor
**System:** Art
**Regression:** no (new feature — plan 008)
**Environment:** Unity Editor, Windows

#### Steps to Reproduce
1. Enter Play Mode on `Assets/Scenes/SampleScene.unity`.
2. Look at the `Bench` GameObject's rendered sprite (`Bench_01.png`), particularly the backrest.

#### Expected / Actual
The knockout pass should remove all near-white background pixels, leaving only the bench's opaque ink lines, matching every other sprite in the roster. / Visible white leftovers remain on the back of the bench sprite.

#### Evidence
Reported directly by the user (Maria) while observing Play Mode live.

#### Suspected cause
Plan 008 Task 1.2 used a manually-tuned `whiteThreshold` (195 for `Bench.png`) chosen by histogram inspection to clear a photo's soft gray backdrop gradient/cast shadow — per that task's own report, this was empirically chosen and confirmed "stable across 190–220," but evidently some near-white pixels around the backrest fall outside that cleared range. Re-tuning the threshold (or a per-region threshold) is likely needed.

---

### Bug: Bench GameObject is positioned too high, reads as floating

**Severity:** Minor
**System:** Art / Gameplay (scene setup)
**Regression:** no (new feature — plan 008)
**Environment:** Unity Editor, Windows

#### Steps to Reproduce
1. Enter Play Mode on `Assets/Scenes/SampleScene.unity`.
2. Look at the `Bench` GameObject relative to the ground plane.

#### Expected / Actual
The bench should rest on the ground surface, with its near (left) leg reading closer to the viewer/lower and its far (right) leg reading further away, and roughly the midpoint of the bench at surface level (per the user's description of the intended perspective). / The bench currently sits noticeably above the ground line, reading as floating.

#### Evidence
Reported directly by the user (Maria) while observing Play Mode live. Current `Bench` GameObject Transform: position `(0, 0, 0)`, `SpriteRenderer` bottom-pivot sprite (`Bench_01.png`, pivot `(0.5, 0)`) — placed directly on the shared ground line (world y=0) per plan 008 Task 5.2, same convention as `Player`.

#### Suspected cause
The sprite's own bottom-pivot convention places its lowest opaque pixel row at local y=0, but per the user's description the bench's *legs* (not necessarily the sprite's absolute lowest pixel, if the reference photo's perspective/shadow extends below the legs) should read as touching the ground — worth re-deriving the correct Y offset (or re-cropping the sprite's bottom edge) once Bug 1's contamination is also resolved, since the two may interact (a wider near-white margin at the bottom could be inflating the sprite's apparent height/pivot placement).

---

### Bug: Quirrel doesn't visually sit "on" the bench — two disconnected bench fragments

**Severity:** Major
**System:** Art / Gameplay (design)
**Regression:** no (new feature — plan 008)
**Environment:** Unity Editor, Windows

#### Steps to Reproduce
1. Enter Play Mode, stand anywhere along the bench's footprint, press `W`.

#### Expected / Actual
Quirrel should read as sitting on the one visible bench, centered on it. / Quirrel sits wherever he was standing (plan 008 Decision 3's "sit anywhere along the footprint, no centering required"), but `Quirrel_Sitting_01.png` has partial bench material baked into the sprite itself (an accepted trade-off named in plan 008 Task 1.2, Zones 1/2). The result is two visually disconnected bench fragments: the real `Bench_01` prop, and the partial bench behind Quirrel's own sprite — they don't line up into one coherent image.

#### Evidence
Reported directly by the user (Maria) while observing Play Mode live; also independently reproduced during this session's own live verification (see the orchestrator's note above about an unrelated Animator-state test artifact that looked similar but was confirmed to be a different, non-shippable cause — this bug is the real, reachable one).

#### Suspected cause / Correction to the design requirement
Plan 008 Decision 3's "no centering required" was **the wrong requirement** — confirmed directly by the user (Maria), overriding that decision. The correct, fully-specified fix (per the user, 2026-08-05):

- On a successful `TrySit`: Quirrel's transform snaps to the bench's sit anchor (so the composite `Quirrel_Sitting_01` art — which already has bench geometry baked in from the source photo — lines up exactly where the real bench is); Quirrel's own idle/walk `SpriteRenderer` visual is replaced by the sitting pose (already true via the Animator); and the **standalone `Bench_01` sprite is hidden** for the duration of the sit (its `SpriteRenderer` disabled, or equivalent) — there is deliberately no attempt to re-crop `Quirrel_Sitting_01.png` to remove its baked-in bench material and composite it against the separately-rendered `Bench_01`; the composite sprite IS the sitting visual, in full, bench included.
- On standing up (`StandUpIfWalking`, or `Hurt()`/`Die()` interrupting): Quirrel's normal idle/walk sprite reappears (already true), and the **`Bench_01` sprite is shown again** (now visibly empty, since Quirrel has moved off it).

This is a real code change (not just an art fix): `TrySit` needs a way to know which specific `Bench` it succeeded against (currently `PlayerController.IsNearBench` is a plain bool with no reference to a specific `Bench` instance — plan 008 Decision 7 already named single-bench-only as an accepted limitation, so this fix should resolve that reference cleanly, e.g. `IsNearBench` becoming a `Bench` reference or an added `Bench NearBench` property) so it can (a) read that bench's sit-anchor position/`Transform` to snap Quirrel to, and (b) toggle that specific bench's `SpriteRenderer.enabled` on sit-start/stand-up. The "sit from either edge of the footprint, not centered" acceptance criteria from plan 008 Task 1.2/6.1 (and this doc's own 5d.2 row) need to be revised to assert the snap-and-hide behavior instead. Route through the pipeline as a plan 008 follow-up (Draft → Review → Implement), not a silent patch.

## 6. Sign-off

- [ ] All items in Sections 1–5 checked PASS
- [ ] Any FAIL has a corresponding bug report filed against @gameplay-programmer (Jump/Attack/Hurt/Die logic) or @art-director (clip timing/feel) as appropriate, per the standard bug report format
- [ ] If Option B (temporary `[ContextMenu]` attributes) was used for Section 0, confirm those attributes were reverted out of `PlayerController.cs` before this feature is handed to @build-engineer
- [ ] Tester name/date recorded below

**Tester:** ______________________ **Date:** ______________________ **Result:** PASS / FAIL
