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

## 6. Sign-off

- [ ] All items in Sections 1–5 checked PASS
- [ ] Any FAIL has a corresponding bug report filed against @gameplay-programmer (Jump/Attack/Hurt/Die logic) or @art-director (clip timing/feel) as appropriate, per the standard bug report format
- [ ] If Option B (temporary `[ContextMenu]` attributes) was used for Section 0, confirm those attributes were reverted out of `PlayerController.cs` before this feature is handed to @build-engineer
- [ ] Tester name/date recorded below

**Tester:** ______________________ **Date:** ______________________ **Result:** PASS / FAIL
