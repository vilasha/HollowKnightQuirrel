# Implementation Plan — Player Control Rebind: Arrows/Z/X → A/D/J/K

**Status:** ✅ APPROVED — implementation-plan-reviewer, round 3
**Author:** implementation-plan-architect
**Date:** 2026-08-04
**Feature:** Rebind three of PlayerController's four input keys. Jump (Space) is explicitly
**unchanged**.

| Action | Current | New |
|---|---|---|
| Move left | `KeyCode.LeftArrow` | `KeyCode.A` |
| Move right | `KeyCode.RightArrow` | `KeyCode.D` |
| Attack (`GetKeyDown`, fires `TryAttack`) | `KeyCode.Z` | `KeyCode.J` |
| Defend (`GetKey`, held, sets `DefendHeld`) | `KeyCode.X` | `KeyCode.K` |
| Jump (`GetKeyDown`, fires `TryJump`) | `KeyCode.Space` | **unchanged** |

**Revision history:**
- **Round 1** (implementation-plan-reviewer): NEEDS REVISION — architecture/breakdown accepted,
  but the location enumeration in two touched files was narrower than the acceptance-criteria
  greps meant to police it. Fixed by re-running unscoped full-text searches (not targeted greps)
  across all three touched files: added `002_quirrel-sprite-animation-player-control.md` §1.12
  (plus §1.6/§1.7/§1.11, found by the broader search), 3 missed lines in
  `PlayerControllerPlayModeTests.cs`, and 3 missed rows (2.3/3.1/3.2) in
  `002_manual-playtest-protocol.md`. Also accepted the non-blocking suggestion to move
  doc-addendum work out of `[GAMEPLAY]` Task 1 into its own `[QA]` task (Task 3), keeping
  `[GAMEPLAY]` scoped strictly to `PlayerController.cs`.
- **Round 2** (implementation-plan-reviewer): NEEDS REVISION, one remaining gap — the reviewer's
  own full `\bLeft\b|\bRight\b` / `\bX\b|\bZ\b` sweep of the whole
  `002_quirrel-sprite-animation-player-control.md` file found that **§1.8** ("Attack/Defend
  'commit in place' decision," ~line 149: "Both Jump/Space **and** Left/Right horizontal input
  are ignored...") was the one remaining `## 1.` design-decision section not yet in Task 3's
  addendum list. Sections 1.1–1.5, 1.9, 1.10, 1.13–1.17 were confirmed clean. Fixed below: §1.8
  added as a sixth addendum location in Task 3, with the same "pointer note only, no rewrite"
  treatment as the other five. Non-blocking feedback also addressed: Task 3's own prose now
  states explicitly (not just implied) why the ~13 old-key mentions in the Phase 3/4 checklists
  are deliberately excluded, rather than leaving that large untouched surface to be mistaken for
  another miss.
- **Round 3** (implementation-plan-reviewer): ✅ APPROVED — §1.8 wording and the "~13 further
  old-key mentions" count both independently re-verified against the file; nothing else changed
  unexpectedly since round 2.

---

## 0. Summary and verified blast radius

This is a keyboard-remap only: four `KeyCode` literals inside `PlayerController.Update()`, plus
the documentation/comments/plan records that name them. No Animator parameter, no serialized
field, no prefab, no scene, no tag/layer, and no persisted/save state is touched —
`Hurt`/`Die`/`Attack`/`Defend`/`Jump` remain triggered by the same Animator parameter names as
before; only the *physical key* that raises each one changes.

**Complete write list for this whole plan** (anything outside this list found during
implementation is a scope escape and should be flagged, not silently absorbed):

| Path | Written by | Why |
|---|---|---|
| `Assets/Scripts/Player/PlayerController.cs` | Task 1 | The 4 real `KeyCode` literals + 2 doc-comment mentions of the old keys |
| `Assets/Scripts/Player/Tests/EditMode/PlayerControllerTests.cs` | Task 2 | Stale comment/assertion-string text naming the old keys (no logic changes) |
| `Assets/Scripts/Player/Tests/PlayMode/PlayerControllerPlayModeTests.cs` | Task 2 | Same, PlayMode side (3 locations missed in round 1, now included) |
| `Docs/Plans/002_quirrel-sprite-animation-player-control.md` | Task 3 | Addenda at the "Control scheme" bullet list, §1.6, §1.7, §1.8, §1.11, and §1.12 — pointer notes only, original decision text preserved |
| `Docs/Plans/002_manual-playtest-protocol.md` | Task 4 | QA tester-facing key instructions (Sections 1–5) |

**Confirmed out of scope — verified by grep/read, not assumed:**
- No other script under `Assets/Scripts/` references `KeyCode` or `Input.Get*` (checked
  `Assets/Scripts/Camera/*` and all of `Assets/Scripts/Player/*` — only the 3 code/test files
  above hit).
- No HUD/tooltip/pause-menu/on-screen text anywhere in `Assets/Scenes/SampleScene.unity`
  displays "Arrow Keys", "Z", "X", or "Space" as a player-facing instruction — there is no
  in-game control-hint UI yet, so no `[UI]` task is needed.
- No project README exists (only third-party package READMEs under `Library/PackageCache`).
- `com.unity.inputsystem` is not installed — there is no Input Actions asset to touch, and no
  `[DATA]` task is triggered.
- `AnimatorContractTests.cs` is unaffected — it tests Animator parameter names/hashes and the
  `JumpAnticipation` state name, none of which change.
- No serialized field, ScriptableObject, prefab, tag, physics layer, or the collision matrix is
  touched. No save schema exists yet in this project, so no migration task is triggered.
- Round 1's false-positive check re-confirmed: `PlayerControllerPlayModeTests.cs` lines ~403/422
  and `002_manual-playtest-protocol.md` rows 3.3/3.4 all say "X position" meaning the coordinate
  axis, not the Defend key — these are correctly left unchanged.

**Why a live-input verification task is unavoidable, not optional:** EditMode tests call
`TryJump`/`TryAttack`/`ApplyHorizontalMovement` directly, and PlayMode tests write
`_horizontalInput`/`DefendHeld` via reflection — neither path ever calls
`Input.GetKey(KeyCode.___)`, because legacy `UnityEngine.Input` cannot be driven
programmatically in this project (no hardware-simulation layer, no Input System package —
already documented in `PlayerControllerPlayModeTests.cs`'s class doc comment). This means the
existing automated suites are **structurally blind to which physical key is wired** — they would
pass identically whether Task 1 remapped correctly, remapped to the wrong letters, or did
nothing at all. The only real proof that A/D/J/K (and *only* those, not the old keys too) work
is a live Play Mode check. That is why Task 4 below is mandatory, not a "nice to have."

---

## Phase 1 — Rebind, comment/doc consistency, and live verification

#### Task 1: [GAMEPLAY] Remap the four KeyCode literals in PlayerController.cs
**Depends on:** none
**Parallel:** yes — with Task 3
**Touches:** `Assets/Scripts/Player/PlayerController.cs` only
**Regression risk:** Low and narrowly scoped — 4 literal-value changes inside `Update()`, no
signature change, no new/removed field, no Animator parameter touched. Confirmed NOT a
serialized-field, prefab, ScriptableObject, tag/layer, Animator-parameter, or save-schema
change (see Section 0's "confirmed out of scope" list) — none of this project's standard Unity
breakage vectors apply. The only real risk is a copy-paste swap (e.g. accidentally wiring `A` to
Attack instead of Move-left) — mitigated by the explicit key-by-key acceptance criteria below.

Change exactly these four lines:
- `Input.GetKey(KeyCode.LeftArrow)` → `Input.GetKey(KeyCode.A)` (line ~253)
- `Input.GetKey(KeyCode.RightArrow)` → `Input.GetKey(KeyCode.D)` (line ~258)
- `Input.GetKeyDown(KeyCode.Z)` → `Input.GetKeyDown(KeyCode.J)` (line ~276)
- `Input.GetKey(KeyCode.X)` → `Input.GetKey(KeyCode.K)` (line ~285)
- Line ~287 (`Input.GetKeyDown(KeyCode.Space)`, `TryJump`) — **do not touch**.

Also update the two doc-comment mentions of the old keys in this same file, since this file's
own documentation would otherwise misdescribe itself:
- File header (~line 10): "Attack (Z), Defend (X)" → "Attack (J), Defend (K)".
- `DefendHeld`'s doc comment (~line 56): "true while X is held" → "true while K is held".

**Acceptance criteria:**
- [ ] All 4 target lines use the new `KeyCode` and compile with zero warnings
- [ ] `Input.GetKeyDown(KeyCode.Space)` on the Jump line is byte-for-byte unchanged
- [ ] A grep for `KeyCode.LeftArrow`, `KeyCode.RightArrow`, `KeyCode.Z`, `KeyCode.X` under `Assets/Scripts/Player/PlayerController.cs` returns zero hits
- [ ] The file-header and `DefendHeld` doc comments no longer name Z/X; they name J/K
- [ ] **Regression check:** the full existing EditMode suite for this feature (`Quirrel.Player.EditModeTests` — 20 tests across `PlayerControllerTests`, `JumpPhysicsMathTests`, `AnimatorContractTests`) and the full existing PlayMode suite (`Quirrel.Player.PlayModeTests` — 8 tests in `PlayerControllerPlayModeTests`) all still pass, unmodified, with no new failures — expected, since none of them call `Input.GetKey` directly, but must be confirmed rather than assumed

---

#### Task 2: [QA] Update stale key references in the Player EditMode/PlayMode test comments and assertion strings
**Depends on:** Task 1
**Parallel:** yes — with Task 3, Task 4
**Touches:** `Assets/Scripts/Player/Tests/EditMode/PlayerControllerTests.cs`, `Assets/Scripts/Player/Tests/PlayMode/PlayerControllerPlayModeTests.cs`
**Regression risk:** None — every change in this task is a comment or a human-readable
assertion-failure-message string. No test logic, no reflection field/property names, no
`[Test]`/`[UnityTest]` method bodies change. Confirmed via full-file text search (not a
targeted grep) that neither file calls `Input.GetKey`/`GetKeyDown` anywhere — both drive input
via direct public-method calls or reflection into `_horizontalInput`/`DefendHeld` — so there is
no simulated-input code to rewire, only prose to correct.

**Complete location list (re-verified via unscoped full-text search of both files, superseding
the round-1 draft's narrower list):**

`PlayerControllerTests.cs` (EditMode):
- Line ~43: "simulate one frame of Right-arrow input" → "D"
- Line ~167: `ForceSetDefendHeld`'s doc comment literally says `Input.GetKey(KeyCode.X)` → `KeyCode.K`
- Line ~277, ~296: "simulate held Right-arrow" → "D"
- Line ~316, ~321: assertion strings "First Z press...", "second Z press..." → "J press"

`PlayerControllerPlayModeTests.cs` (PlayMode) — **includes the 3 locations round 1 missed**:
- Line ~33 (class doc comment): "(Left/Right arrow, X/Defend)" → "(A/D, K/Defend)"
- Line ~138, ~250: "simulated held Right-arrow" comments → "D"
- Line ~153, ~259: assertion strings naming "Right-arrow" → "D"
- Line ~278: assertion string "First Z press..." → "J press"
- Line ~312: "simulated held X" comment → "simulated held K (Defend)"
- Line ~317: comment "resets DefendHeld from live Input.GetKey(X)" → `Input.GetKey(K)` *(round-1 miss)*
- Line ~328: comment "simulate the X-up edge" → "K-up edge" *(found during round-1's broader search, same two-line comment block as the next item)*
- Line ~329: comment "the instant Input.GetKey(X) goes false" → `Input.GetKey(K)` *(round-1 miss)*

**Explicitly left unchanged, confirmed not key references:** lines ~403/~422 ("X position must
not change") refer to the transform's X-axis coordinate, not the Defend key — do not touch.

**Acceptance criteria:**
- [ ] A grep across both test files for `Right-arrow`, `Left-arrow`, `Z press`, `KeyCode\.X`, `GetKey\(X\)`, `X-up`, `held X`, `X/Defend` returns zero hits (broadened per round 1 — the previous narrower grep is exactly what missed the 3 PlayMode locations)
- [ ] Every updated comment/string reads consistently as A (left) / D (right) / J (attack) / K (defend) — no mixed old/new terminology left in a single file
- [ ] Lines ~403/~422's "X position" text is confirmed unchanged (coordinate reference, not a key reference — do not "fix" it)
- [ ] No `[Test]`/`[UnityTest]` method signature, assertion logic, or reflection field/property name changed — diff is confined to comments and string-literal message text
- [ ] **Regression check:** re-run both suites after this task; identical pass count to Task 1's post-change run (20 EditMode / 8 PlayMode), confirming the comment-only edits introduced no accidental syntax breakage

---

#### Task 3: [QA] Add rebind pointer addenda to the original feature plan's decision sections
**Depends on:** none
**Parallel:** yes — with Task 1, Task 2, Task 4
**Touches:** `Docs/Plans/002_quirrel-sprite-animation-player-control.md` only
**Regression risk:** None to code — this is a historical-record consistency task. The risk it
guards against is a future reader (human or agent) consulting this plan's
§1.6/§1.7/§1.8/§1.11/§1.12 or its "Control scheme" section as a live reference for "what are the
current bindings" and being misled, since those sections independently restate Z/X/arrows as
ongoing fact rather than as a dated decision.

**Scope, and why it's an addendum rather than a rewrite:** per this repo's own established
convention (`001_pin-head-recolor.md`'s status block), a completed/accepted plan's original
decision text is preserved as an accurate record of what was decided *at the time* — it is not
retroactively edited to match later changes. This task therefore adds short pointer notes only;
it does not change a single word of the original decision prose in any of the six locations
below.

**Complete location list (re-verified across two rounds via unscoped full-text search of the
whole 575-line file — round 1 searched for `Arrow|Space|KeyCode|GetKey|Z|X|Left|Right`; round 2's
reviewer independently re-swept with `\bLeft\b|\bRight\b` and `\bX\b|\bZ\b` and confirmed
sections 1.1–1.5, 1.9, 1.10, 1.13–1.17 are clean and that §1.8 was the one remaining gap):**
1. **"Control scheme (locked in before drafting began)"** bullet list (~lines 24–31) — round 1.
2. **§1.6 "Animation clip authoring"** table (~line 115): "`Quirrel_DefendHold` ... n/a — held
   while X down" — round 1.
3. **§1.7 "Animator parameter contract"** table (~lines 127–129): "`JumpTrigger` ... on Space
   press" (Space unaffected, no note needed on this row), "`AttackTrigger` ... on Z press",
   "`DefendHeld` ... true while X held" — round 1. **This table is the one
   `AnimatorContractTests.cs` itself cites by section number** — the highest-traffic of these
   sections.
4. **§1.8 "Attack/Defend 'commit in place' decision"** (~line 149): "Both Jump/Space **and**
   Left/Right horizontal input are ignored — the character does not move and cannot leave the
   ground." — **round 2, the reviewer's own sweep found this was the one section round 1's list
   missed.**
5. **§1.11 "Explicit deferrals"** bullet (~line 175): "releasing X snaps directly back to
   locomotion" — round 1.
6. **§1.12 "Input system"** (~line 179) — round 1's originally-flagged section: "hardcoded to
   `LeftArrow`/`RightArrow`/`Space`/`Z`/`X`. Adopting the new Input System is out of scope — key
   rebinding will require a code change in v1, not a data asset, noted as a known limitation."
   This line is the most load-bearing of the six to correct, since it explicitly frames the old
   keys as an ongoing limitation rather than a historical value.

**Explicitly excluded from this task — stated here directly, not left implied, precisely
because six separate hits were already found in this same document and an unstated exclusion
of a much larger surface would be easy to mistake for a seventh miss:** the Phase 3/4 task
acceptance-criteria checklists (~lines 346–487) contain roughly **13 further old-key mentions**
(e.g. "Left/Right arrow keys move the character...", "Z press (`GetKeyDown`, edge) fires
`AttackTrigger`", "X held sets `DefendHeld` true", "simulated Right-arrow hold for 1s moves the
Player", "Z press fires exactly one Attack per press", among others). **None of these 13 are
addendum locations, and none should be touched by this task.** They are the historical execution
record of the *already-completed* original implementation — the same category as the "Control
scheme" bullets — documenting what was true and specified at build time, not an ongoing
architectural reference. This is a deliberate scoping boundary, confirmed correct by the round-2
reviewer, not an oversight to fix later.

**How to add the notes:** one addendum block placed immediately under the "Control scheme"
heading, listing all six locations above (1–6) with a one-line pointer to this plan's number
(`Docs/Plans/003_control-rebind-adjk.md`); plus one shorter inline pointer directly at §1.12,
since that section's wording ("key rebinding will require a code change in v1") reads as active
guidance a future implementer might otherwise still follow literally.

**Acceptance criteria:**
- [ ] An addendum block exists directly under the "Control scheme" heading, referencing all 6 locations above by section number and current line content
- [ ] A second, shorter pointer note exists directly inside §1.12, immediately following its original sentence
- [ ] Diff shows only additions — not one character of the original decision prose in the "Control scheme" bullets, §1.6, §1.7, §1.8, §1.11, or §1.12 is altered or removed
- [ ] The Phase 3/4 acceptance-criteria checklists (~lines 346–487, ~13 further old-key mentions) are confirmed byte-identical (untouched) — this is the explicit "do not touch" boundary of this task, and the plan's own prose states why (historical execution record), not just its acceptance criteria
- [ ] File remains LF-only, no BOM, matching this repo's existing convention for `.md` files under `Docs/Plans/` (confirmed already the case for the sibling `001_pin-head-recolor.md`)
- [ ] A full-text re-grep of the file for `LeftArrow|RightArrow|Z|X` after this task's edits shows the same original hit locations as before (the 6 addendum locations plus the 13 excluded Phase 3/4 mentions), each addendum location now immediately followed by (or preceded by, for §1.12) a pointer note — not a shrunk or altered hit set

---

#### Task 4: [QA] Update the manual playtest protocol's key instructions and re-verify live
**Depends on:** Task 1 (the live-verification half needs the real code change in place; the
text-update half has no code dependency but is bundled here as one QA pass)
**Parallel:** yes — with Task 2, Task 3 (different files; in practice this is the natural last
task to run, as the final confirmation, though it is not a hard file/dependency conflict with
the others)
**Touches:** `Docs/Plans/002_manual-playtest-protocol.md` only
**Regression risk:** None to code. The risk this task guards against is procedural: a QA tester
following the existing doc verbatim would press Z/X/arrows against the rebound controller and
misread "nothing happened" as a functional regression, when it is actually a stale-instructions
problem. This is also the only task in this plan that actually exercises the new keys against a
live `Input.GetKey`/`GetKeyDown` call (see Section 0's rationale for why this can't be
automated).

**Complete row list (re-verified via unscoped full-text search of the whole 134-line file for
`Arrow|Space|KeyCode|GetKey|Z|X|Left|Right`, superseding the round-1 draft, which omitted rows
2.3, 3.1, 3.2 — exactly the core walk/boundary tests):**
- Row 1.3 ("press Z once"), 1.4 ("Rapidly press Z"), 1.5 ("Hold X", "Release X"), 1.7 ("arrows,
  Space, Z, X")
- Row 2.1 ("Press and hold Left, then press Space"), 2.2 ("hold Right, press Space")
- **Row 2.3 ("press and hold Left or Right") — missed in round 1**
- **Row 3.1 ("Hold Left arrow continuously") — missed in round 1**
- **Row 3.2 ("mirrored with Right arrow") — missed in round 1**
- Row 3.3 ("Press Z to start an attack"), 3.4 ("Hold X to enter Defend", "Release X")
- Row 4.1, 4.3 — pure Space rows; **left unchanged**, Jump is not being rebound
- Row 5.1 ("Press Z to start an attack"), 5.3 ("press Z"), 5.4 ("press Z several more times")

Update every one of the above (excluding 4.1/4.3) to A/D for movement and J/K for
Attack/Defend. Row 3.3/3.4's "X position is unchanged" wording (the coordinate axis, not the
Defend key) is confirmed correct as-is and must not be touched.

After the text is updated, re-run the subset of checks that specifically exercise the rebound
keys (1.3–1.5, 2.1–2.3, 3.1–3.4, 5.1/5.3/5.4 — skipping the pure-Space rows 1.1/1.2/4.1/4.3,
since Jump is unchanged and was already verified live in the original pass) against
`Assets/Scenes/SampleScene.unity` in Play Mode — via Unity MCP `execute_code`/input simulation
if a live Unity Editor session is connected, otherwise by hand at the keyboard — confirming:
(a) each new key (A, D, J, K) produces the same behavior the corresponding old key used to, and
(b) the **old keys (Left/Right Arrow, Z, X) now do nothing at all** — a rebind that leaves the
old key still working alongside the new one is a silent double-binding, not a clean remap, and
would not be caught by any other task in this plan.

**Acceptance criteria:**
- [ ] A grep of the updated document for `Press Z`, `press X`, `Hold X`, `hold X`, `Left arrow`, `Right arrow`, singular `arrow`, and bare `Left`/`Right` immediately after `Hold`/`hold`/`Press`/`press` returns zero hits outside of Section 4 (Space-only) and the unrelated "X position" wording in rows 3.3/3.4
- [ ] **A full manual read-through of every row's Steps column in Sections 1–5 is performed after the grep** (not grep alone — a narrower grep is exactly what caused this plan's round-1 gaps in rows 2.3/3.1/3.2), confirming each names A/D/J/K correctly, including row 2.3 (direction change mid-air), 3.1 (walk off left edge), and 3.2 (walk off right edge) specifically
- [ ] Every updated row's Steps column names A/D for movement, J for Attack, K for Defend
- [ ] Live verification performed and recorded (PASS/FAIL, per this doc's own existing sign-off convention) for: A moves left, D moves right, J triggers a single attack per press with no stacking on rapid presses, K holds Defend and releases cleanly, diagonal Space+A and Space+D both still work, mid-air direction change with A/D works (row 2.3), walking continuously off either edge with A/D works (rows 3.1/3.2)
- [ ] Live verification confirms the **old keys are fully dead**: holding Left Arrow/Right Arrow produces no movement, pressing Z produces no attack, holding X does not enter Defend
- [ ] No FAIL is left unresolved — any FAIL found here is logged as a bug per this doc's existing bug-report format and routed back through the pipeline, not silently patched inside this task

---

```
Phase 1 dependency graph:
  Task 1 [GAMEPLAY] (PlayerController.cs)              ──→ Task 2 [QA] (test file comments)
  Task 3 [QA] (002_quirrel-sprite-...-control.md addenda) ── independent, parallel with all
  Task 1 [GAMEPLAY]                                     ──→ Task 4 [QA] (manual playtest protocol + live verify)
  (Task 1/Task 2/Task 3/Task 4 all touch disjoint files — no shared scene/prefab conflict;
   Task 2 and Task 4 are sequenced after Task 1 only because their acceptance criteria include
   a live/automated regression check against Task 1's actual code change)
```

## Explicitly out of scope for this plan
- Any in-game control-hint UI (none exists yet — if one is added later, it becomes its own `[UI]` task against the real Space/A/D/J/K scheme, not retrofitted here).
- Rebindable/configurable controls, an options menu, or migrating to `com.unity.inputsystem` — this plan only swaps hardcoded `KeyCode` literals, matching the existing architecture; a full input-system rearchitecture is a separate, much larger plan and is not warranted by this request.
- Any change to Jump/Space, to Animator parameter names, or to any serialized Inspector value.
