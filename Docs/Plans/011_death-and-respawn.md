# Implementation Plan — Death and Respawn at the Last-Rested Bench

**Status:** ✅ APPROVED (Revision 2, accepted by implementation-plan-reviewer)
**Author:** implementation-plan-architect
**Date:** 2026-08-13
**Feature:** When Quirrel's health reaches 0, `PlayerController.Die()` (currently a permanent dead-end) starts a fixed delay, then automatically repositions him standing at the Bench he last rested at, restores a full Mask bar via the existing `PlayerHealth` heal path, and resets the Animator back to `Idle` — all within the single already-loaded scene, with no new scene, save, or UI system.

**Revision history:**
- **Revision 1** (draft): initial plan from implementation-plan-architect.
- **Revision 2** (this version): addressed implementation-plan-reviewer's NEEDS REVISION verdict on Revision 1 — added per-phase dependency graphs; added a fast, targeted EditMode test isolating the `IsDead=false`-before-`Respawned.Invoke()` ordering invariant instead of relying only on a slower PlayMode integration test; pinned `AdvanceRespawnTimer`'s exact `FixedUpdate` call-site position with an explicit ordering-rationale comment; named the specific `MissingReferenceException` crash mode a destroyed-but-fake-non-null Bench would cause via the interface-typed `LastRestedBenchSeat`; renamed a stale test method name; named `Die()`'s missing re-entrancy guard as an accepted, currently-unreachable edge case.
- **Reviewer's final pass** on Revision 2: **ACCEPT**. All four flagged items confirmed genuinely fixed (not just asserted) against the live repo — dependency graphs, the ordering-invariant test (independently confirmed to also protect a real downstream consumer: `PlayerHealth.Heal()`'s own `IsDead` gate would otherwise silently no-op the heal-on-respawn if the two lines were ever swapped — noted here per the reviewer's suggestion), the `FixedUpdate` call-site pin, and the crash-mode naming. Bench-destruction claim re-confirmed via independent grep (only test-teardown `DestroyImmediate` calls exist; no gameplay code destroys a `Bench`). The stale-test-method rename confirmed safe (no other reference to the old name anywhere in the repo).

---

## 0. Summary and verified blast radius

**Confirmed from the actual source (read directly, independently re-verified by implementation-plan-reviewer across both review rounds):**

- `Assets/Scripts/Player/PlayerController.cs`. `Die()` unconditionally sets `IsDead = true`, fires `DieTrigger`/`IsDead` on the Animator; no code path anywhere ever sets `IsDead` back to `false` today. `Hurt()` no-ops entirely while `IsDead` (`if (IsDead) return;`, confirmed at the top of that method); every `Try*` method (`TryJump`, `TryAttack`, `TrySit`), the `DefendHeld` continuous-read line, and `UpdateLookState`'s `LookingUp`/`LookingDown` (via `IsIdleAndGrounded()`) already gate on `!IsDead` directly or transitively.
- **Guard-flag trace — independently re-verified by the reviewer, line-by-line, and found tighter than originally claimed.** `TryAttack`, `TryJump`, `TrySit`→`IsIdleAndGrounded`, the `DefendHeld` assignment line, and `Hurt()`'s own `IsDead` guard all confirmed to gate correctly — none of `_isAttacking`, `_jumpInProgress`, `DefendHeld`, `LookingUp`/`LookingDown`, `_isHurtStunned`, `_isSitting` can be re-armed while `IsDead` is true. The `_jumpImpulseTimerActive`/`_jumpImpulseCancelled` interaction was independently stress-tested by the reviewer for the exact edge case where `Die()` interrupts mid-jump-anticipation and `Respawn()` fires before that window naturally resolves: `AdvanceJumpTimer` keeps ticking unconditionally regardless of `IsDead` and resolves on its own fixed 0.08s schedule; `ApplyJumpImpulseIfValid` checks `IsDead` first (blocking under the default 1.5s respawn delay) **and**, in a hypothetical future where `_respawnDelay` were tuned below 0.08s, `_jumpImpulseCancelled` (set `true` by `Die()`, only ever cleared by the *next* `TryJump()` call) is a second, independent gate in the same method that still blocks the stale impulse. This is doubly-guarded, not singly-guarded as originally described.
- `Assets/Animations/Quirrel.controller`: `Die` state (fileID `1102010`) has `m_Transitions: []` — genuinely empty, confirmed. The only inbound edge is `AnyState → Die` (fileID `1101013`) on `DieTrigger`, `m_TransitionDuration: 0`, `m_HasExitTime: 0`, `m_CanTransitionToSelf: 0` — exactly the shape this plan proposes for the new `RespawnTrigger` transition, and independently confirmed to match the existing `HurtRecoveryTrigger→Idle`/`→Fall` transitions' shape. Grepped by the reviewer across all of `Assets/Scripts` for `IsName("Die")` — nothing reads "currently in Die," so an instant mid-clip exit cannot break anything else. `Quirrel_Die.anim` confirmed non-looping (`m_StopTime: 0.5`, `m_LoopTime: 0`) — the proposed 1.5s default delay comfortably exceeds it. `Die`'s Motion clip is already real/authored — no new clip needed.
- `Assets/Scripts/Player/PlayerHealth.cs` (plan 010). `ApplyDamage(int)` calls `PlayerController.Die()` the instant `CurrentQuarterMasks` reaches exactly 0. `FullHeal()`/`Heal()` **no-op if `PlayerController.IsDead` is true** — this is the load-bearing fact behind Task 1.2's ordering test (item 2 below), independently confirmed by the reviewer to be a real, not merely abstract, downstream risk: if `Respawn()`'s `IsDead = false` and `Respawned?.Invoke()` lines were ever swapped, Task 3.1's heal-on-respawn subscription would silently no-op. `PlayerController.Rested` (`System.Action`) is subscribed `Rested += FullHeal` in `OnEnable`/unsubscribed in `OnDisable` — this plan's `Respawned` event mirrors this exact pattern.
- `Assets/Scripts/Environment/Bench.cs` / `Assets/Scripts/Player/IBenchSeat.cs`: **exactly one `Bench` GameObject exists in `Assets/Scenes/SampleScene.unity`**, confirmed independently by the reviewer via the component's guid. Bench world trigger box: x∈[-0.685,0.685], y∈[-0.13,2.87]. Player world collider box (at spawn): x∈[-0.5,0.5], y∈[0,1.31]. **The Player's entire collider — not merely its origin point — sits inside the Bench's trigger volume**, confirmed by the reviewer's own independent arithmetic. This means `CheckInitialSpawnSit`'s existing one-shot spawn-time auto-sit already fires at session start today, so `Rested` already fires frame one — `LastRestedBenchSeat` tracking relies on this for "always has a real bench" in practice. `PlayerHealth` is independently confirmed already attached to the `Player` GameObject in-scene (plan 010) — supporting this plan's "no `SampleScene.unity` edits needed" claim. `IBenchSeat` exposes exactly `SitAnchor` (Transform) and `SetVisible(bool)` — confirmed sufficient for what `Respawn()` needs (it never calls `TrySit` or touches `SetVisible`, see Decision 4).
- No save/persistence, no scene reload infrastructure, no checkpoint system, no multi-bench system, and no UI prompt system exists anywhere (reconfirmed via grep, independently, by the reviewer, across both review rounds). This plan requires zero new persisted state, zero `[DATA]` tasks — same-scene in-memory teleport only.
- **Bench destruction, re-confirmed in the final review pass:** grepped the full `Assets/` tree for `Destroy` — the only places a `Bench` GameObject is ever destroyed are EditMode test teardowns (`BenchTests.cs`, `Object.DestroyImmediate`), never gameplay code. This directly supports Decision 12's risk-acceptance below.

**Complete write list for this plan:**

| Path | Written by | Why |
|---|---|---|
| `Assets/Scripts/Player/PlayerController.cs` | Task 1.1, 1.2 | +`LastRestedBenchSeat` tracking, +spawn-position fallback, +`Respawn()`/`AdvanceRespawnTimer()`/`Respawned` event, +one new line each in `TrySit()` and `Die()`, +one new call in `FixedUpdate()` (pinned alongside `AdvanceHurtStunTimer` — see Task 1.2) |
| `Assets/Scripts/Player/PlayerHealth.cs` | Task 3.1 | +`Respawned += FullHeal` subscription (mirrors existing `Rested` subscription) |
| `Assets/Animations/Quirrel.controller` | Task 2.1 | +`RespawnTrigger` parameter, +one new `Die → Idle` transition |
| `Assets/Scripts/Player/Tests/EditMode/PlayerControllerTests.cs` | Task 4.1 | New tests |
| `Assets/Scripts/Player/Tests/EditMode/AnimatorContractTests.cs` | Task 4.2 | New parameter-table row + new transition-topology test + one stale test-method rename |
| `Assets/Scripts/Player/Tests/EditMode/PlayerHealthTests.cs` | Task 4.3 | New tests |
| `Assets/Scripts/Player/Tests/PlayMode/PlayerRespawnPlayModeTests.cs` (new) | Task 4.4 | New end-to-end suite |
| `Docs/Plans/002_manual-playtest-protocol.md` | Task 4.5 | New `5g` section |

**Confirmed out of scope:** `Assets/Scenes/SampleScene.unity` is **not touched at all** by this plan; persistence of `LastRestedBenchSeat` across a scene reload or save file; a multi-bench selection system; a "You Died" screen, death counter, or any new UI; a "press any key to respawn" prompt; any Shade/geo-drop-and-recovery mechanic; any new QA test hazard (reuses plan 010's existing `TestDamageHazard`); any `ProjectSettings`/tag/physics-layer/collision-matrix change; any change to `Bench.cs` or the `IBenchSeat` interface itself; fixing the pre-existing `IBenchSeat`-interface "fake null" caveat at its other, pre-existing call sites (named and accepted, not fixed).

**Metroidvania-specific check:** the Bench becomes the de facto single checkpoint by construction (it is the only respawn target that can exist today). No new traversal ability is granted, so no sequence-break risk. Checkpoint integrity: the Bench sits at a static, hazard-free location, so dying anywhere and respawning at the Bench cannot itself strand the player — Task 4.5's manual pass explicitly re-confirms this by dying near plan 010's QA hazard and checking the respawn point is the Bench, not the hazard. Named forward-looking limitation: `LastRestedBenchSeat`'s "whichever bench was last sat on" semantics (Decision 2) already generalize correctly to a future multi-bench scene without further code changes, but nothing in this plan validates a scenario where the *only* known bench becomes physically unreachable after some future one-way traversal element — that risk doesn't exist today and is explicitly a future plan's concern.

---

## 1. Design decisions

**1. Respawn is a same-scene, in-memory teleport — no scene reload, no save/persistence layer.** The project is single-scene with no reload infrastructure, so "remembering" the last-rested bench only needs to survive for the lifetime of the currently-loaded scene, not across sessions.

**2. "Last bench rested at" is tracked via a new `PlayerController.LastRestedBenchSeat` (`IBenchSeat`) reference field**, set inside `TrySit`'s existing success branch alongside the pre-existing `_seatedBench = NearBench;` line — but, unlike `_seatedBench`, **never cleared by `StandUp()`**, so it survives standing up. Only overwritten when `NearBench` is non-null at the moment of a successful sit, so a bare `TrySit(true)` call in the existing test style cannot clobber a previously-good respawn point back to null. **Scoped explicitly to today's single-bench reality** rather than inventing a multi-bench priority/selection system nobody needs yet — the same "name the limitation, don't overbuild" precedent plan 008 Decision 7 already established.

**3. A `_spawnPosition` fallback is captured once, defensively, inside `EnsureCachedComponents()`** as `_rigidbody.position` on the very first call. `Respawn()` uses `LastRestedBenchSeat?.SitAnchor.position ?? _spawnPosition`. **Named, accepted edge case:** this fallback cannot currently be exercised in the shipped `SampleScene` (the Player always spawns inside the one Bench's trigger, so `CheckInitialSpawnSit` always populates `LastRestedBenchSeat` on frame one), but is defended anyway rather than leaving a future scene that spawns the Player away from any bench to `NullReferenceException` on death.

**4. Respawn does NOT auto-seat Quirrel back onto the bench.** It positions him standing at the Bench's `SitAnchor` transform, matching the reference games' own convention (Hollow Knight/Silksong respawn the player standing at a bench, not seated) and keeping this change's surface minimal — zero interaction with `TrySit`/`_isSitting`/`IsNearBench`. **Rejected alternative:** force-call `TrySit` at respawn — rejected because it would require also force-setting `IsNearBench`/`NearBench` ahead of the physics engine's own next trigger evaluation, a race not worth the added complexity.

**5. Fixed delay after death, not player-input-gated.** `_respawnDelay` (serialized tunable, default `1.5f` — confirmed by the reviewer that `Quirrel_Die.anim`'s `m_StopTime: 0.5` means the default 1.5s comfortably exceeds it) seconds after `Die()` runs, `Respawn()` fires automatically. **Rejected alternatives:** instant (would visually skip the already-authored Die animation); player-input-gated (would need a new UI prompt/text system this plan has no reason to pull in).

**6. Timer shape mirrors `AdvanceHurtStunTimer`/`PlayerHealth.AdvanceInvulnerabilityTimer` exactly.** `_isAwaitingRespawn`/`_respawnTimeRemaining`, ticked by `public void AdvanceRespawnTimer(float deltaTime)`, started as the very last action inside `Die()`. **Call-site position, pinned explicitly:** `AdvanceRespawnTimer(Time.fixedDeltaTime)` is called from `FixedUpdate()` immediately after the existing `AdvanceHurtStunTimer(Time.fixedDeltaTime)` call — both are stun/state-recovery timers ticked once per physics step, so grouping them adjacently keeps `FixedUpdate()`'s existing sequence readable, with a one-line comment at the call site (`// Respawn timer grouped with the other recovery-window timer above, same per-physics-step cadence`) matching this file's own established convention. The reviewer independently confirmed the feature is correct regardless of exact insertion order (`ApplyJumpImpulseIfValid`'s double gate protects it either way) — this pin is for future-editor clarity, not because an alternate placement would be unsafe.

**7. `PlayerController` gains exactly one new public event, `Respawned` (`System.Action`)**, invoked at the very end of `Respawn()`, mirroring `Rested`'s exact shape. `PlayerHealth.OnEnable` additionally subscribes `_playerController.Respawned += FullHeal` alongside its existing `Rested += FullHeal`; unsubscribed in `OnDisable`. **Rejected alternative:** `Respawn()` calls `PlayerHealth.FullHeal()` directly — rejected since `Quirrel.Player`'s asmdef has zero outbound references, the identical reason `Rested` exists at all.

**8. Guard-flag disposition (traced against the actual source, independently re-verified line-by-line by the reviewer and found tighter than originally described — see Section 0):** the seven flags already provably safe (`_isAttacking`, `_jumpInProgress`, `DefendHeld`, `LookingUp`, `LookingDown`, `_isHurtStunned`, `_isSitting`) need **no explicit respawn-time reset** — `Respawn()` only needs to touch two things `Die()` legitimately leaves stale: **(a) `IsGrounded`** — `Respawn()` explicitly forces it `true` as a one-frame defensive measure, since the only bench in the scene sits on the ground; **(b) the Rigidbody2D's residual velocity at time of death** — `Respawn()` explicitly zeroes it alongside the position write. `_jumpImpulseCancelled`/`_jumpImpulseTimerActive` need **no** respawn-time handling — see Section 0's expanded, reviewer-strengthened trace. **Named, accepted edge case:** `Die()` itself has no `if (IsDead) return;` re-entrancy guard (unlike `Hurt()`) — a hypothetical *direct* re-entrant `Die()` call while already dead would restart the respawn timer via this plan's new last line. This is unreachable today because `PlayerHealth.ApplyDamage` already gates on `IsDead` before ever calling `Die()` (the only real call site), so `Die()` cannot currently be invoked a second time on an already-dead character — named here explicitly rather than left implicit.

**9. Belt-and-suspenders Animator-bool visual consistency, matching `Hurt()`/`Die()`'s own existing convention.** `Respawn()` explicitly re-sets `DefendHeld`/`LookingUp`/`LookingDown`'s Animator bool parameters and `IsSitting`'s Animator bool to `false`, even though Decision 8 shows the backing code fields are already provably `false` — kept purely for structural symmetry, not load-bearing for correctness.

**10. Animator change is scoped to the `Die` state only, not `AnyState`.** A new `RespawnTrigger` Trigger parameter and exactly one new outgoing transition, `Die → Idle`, condition `RespawnTrigger`, `HasExitTime: 0`, `TransitionDuration: 0`, `CanTransitionToSelf: 0` — instant, matching every other trigger-driven transition already in this controller (independently confirmed by the reviewer to match the existing `HurtRecoveryTrigger` transitions' exact shape). `RespawnTrigger` only has one legitimate firing context (`PlayerController.Respawn()`, itself gated to only run while `IsDead`), so an `AnyState` transition would be strictly wider than needed and would silently inflate every other state's outgoing-transition count that `AnimatorContractTests.cs` already asserts exactly.

**11. `IsDeadHash`'s existing Animator bool is reused, not duplicated.** `Respawn()` calls the same `SetBool(IsDeadHash, false)` this file already uses for the `true` case in `Die()`.

**12. Known Unity "fake null" caveat on `IBenchSeat` — the specific new failure mode, named explicitly.** `LastRestedBenchSeat` is `IBenchSeat`-typed — an **interface**, not a `UnityEngine.Object`-derived class reference. `LastRestedBenchSeat?.SitAnchor` uses the compiler's null-conditional operator, which performs a raw reference-null check — this does **NOT** go through `UnityEngine.Object`'s overloaded `==` operator (that override only ever applies when the *static* type of the expression is `UnityEngine.Object` or a class derived from it, never when the static type is an interface). Concretely: if the underlying `Bench` GameObject were ever destroyed while still referenced by `LastRestedBenchSeat`, Unity's "fake null" pattern does **not** kick in here — the interface reference would still pass the `?.` check as "non-null," and the subsequent `.SitAnchor` property access would throw a `MissingReferenceException` **at the exact moment `Respawn()` runs**, i.e. a hard crash blocking the player from ever coming back from death. This exact class of caveat already exists, unfixed, in this codebase's own pre-existing `_seatedBench`/`Bench.HandleTriggerExit` code — this plan does not introduce a new category of risk, only a new call site that could trigger the pre-existing one. **Accepted as unreachable today**, for the identical reason Decision 3's `_spawnPosition` fallback edge case is accepted: nothing anywhere in this codebase ever destroys a `Bench` GameObject at runtime (confirmed by independent grep in both review rounds — the only `Destroy`/`DestroyImmediate` calls against a `Bench` are EditMode test teardowns, never gameplay code). If a future plan ever adds bench destruction (e.g. a collapsing-room hazard), that plan must also address this interface-typed fake-null gap at the same time, for both this call site and the pre-existing ones.

---

## Phase 1 — `[GAMEPLAY]` `PlayerController.cs` — bench tracking and respawn core

#### Task 1.1: `[GAMEPLAY]` Last-rested-bench tracking + spawn-position fallback capture
**Depends on:** none
**Parallel:** yes — with Task 2.1 (different asset). Not parallel with Task 1.2 (same file)
**Touches:** `Assets/Scripts/Player/PlayerController.cs` — adds `_lastRestedBenchSeat` field + `LastRestedBenchSeat` read-only property, `_spawnPosition`/`_hasCapturedSpawnPosition` fields, one new guarded line inside `EnsureCachedComponents()`, one new guarded line inside `TrySit()`'s existing success branch
**Regression risk:** `TrySit` is existing, heavily tested, order-sensitive across plans 008/009/010. `EnsureCachedComponents()` is called from every public method in this file — the new capture block must be provably idempotent so it never re-fires after the first call.

**Acceptance criteria:**
- [ ] `LastRestedBenchSeat` is `null` on a freshly constructed `PlayerController` before any successful `TrySit`
- [ ] A successful `TrySit(true)` with `NearBench` set to a real `IBenchSeat` sets `LastRestedBenchSeat` to that same reference
- [ ] `LastRestedBenchSeat` is **not** cleared by a subsequent `StandUpIfWalking` — new test, explicitly distinguishing it from `_seatedBench`'s existing clear-on-stand behavior
- [ ] A successful `TrySit(true)` with `NearBench` left `null` does **not** clobber a previously-set `LastRestedBenchSeat` back to `null`
- [ ] `CheckInitialSpawnSit`'s own auto-sit path also populates `LastRestedBenchSeat` when `NearBench` is set at spawn — new test
- [ ] `EnsureCachedComponents()`'s spawn-position capture records the Rigidbody2D's position on its first call only, confirmed unchanged by any subsequent call even after the Rigidbody2D's position later changes — new test
- [ ] Full pre-existing `PlayerControllerTests` suite (plans 002–010) passes unmodified — **regression check**
- [ ] `AnimatorContractTests.cs` full suite re-run and confirmed still green — **regression check**

---

#### Task 1.2: `[GAMEPLAY]` `Respawn()`, respawn delay timer, `Respawned` event
**Depends on:** Task 1.1 (same file; needs `LastRestedBenchSeat`/`_spawnPosition`)
**Parallel:** no — same file as 1.1, sequential. Parallel with Task 2.1 (different asset)
**Touches:** `Assets/Scripts/Player/PlayerController.cs` — adds `_respawnDelay` serialized tunable, `_respawnTimeRemaining`/`_isAwaitingRespawn` fields, `RespawnTriggerHash` static readonly int, `Respawned` event, `public void Respawn()`, `public void AdvanceRespawnTimer(float deltaTime)`; adds exactly one new line at the end of `Die()`'s existing body; adds exactly one new call inside `FixedUpdate()`, positioned immediately after the existing `AdvanceHurtStunTimer(Time.fixedDeltaTime)` call (Decision 6 — both are per-physics-step recovery timers, grouped adjacently with an explicit ordering-rationale comment)
**Regression risk:** `Die()` is existing and tested — the new line must be appended after every existing guard-flag reset/Animator call in `Die()`, none of which may be reordered or altered. `FixedUpdate()`'s existing call order must gain the new `AdvanceRespawnTimer` call at the pinned position without reordering any existing call.

Implementation: `Die()` gains, as its literal last line, `_isAwaitingRespawn = true; _respawnTimeRemaining = _respawnDelay;`. `AdvanceRespawnTimer(float deltaTime)`: no-ops if `!_isAwaitingRespawn`; ticks down; resolves exactly once (`_isAwaitingRespawn = false;` then calls `Respawn()`) — same shape as `AdvanceHurtStunTimer`. `Respawn()`: `if (!IsDead) return;` guard; computes the target position per Decision 3; writes `_rigidbody.position`/zeroes `_rigidbody.velocity` (Decision 8); sets `IsGrounded = true;`; **sets `IsDead = false;` before invoking `Respawned`** — load-bearing ordering, since `PlayerHealth.Heal()`'s own `IsDead` gate means firing `Respawned` one line too early would silently no-op the heal-on-respawn subscription (Task 3.1); force-resets `DefendHeld`/`LookingUp`/`LookingDown` and their Animator bools plus `IsSittingHash`/`IsDeadHash` (Decision 9); fires `SetTrigger(RespawnTriggerHash)`; invokes `Respawned?.Invoke();` last, after `IsDead` is already `false`.

**Acceptance criteria:**
- [ ] `Die()` starts the respawn timer (`_isAwaitingRespawn == true`, `_respawnTimeRemaining == _respawnDelay`) as its last action; every pre-existing guard-flag reset/Animator call in `Die()` confirmed unchanged — **regression check**
- [ ] `AdvanceRespawnTimer` ticked for less than `_respawnDelay` total: `IsDead` remains `true`, `Respawn()` has not run
- [ ] `AdvanceRespawnTimer` ticked past `_respawnDelay`: `IsDead` becomes `false` exactly once, `Respawned` fires exactly once
- [ ] `Respawned`'s invocation is directly observed to occur with `IsDead` already `false` — subscribe a probe (`bool isDeadAtInvocation = true; controller.Respawned += () => isDeadAtInvocation = controller.IsDead;`) before triggering respawn, then assert `isDeadAtInvocation == false` after. This is the fast, direct regression net for the `PlayerHealth.FullHeal()`-no-ops-while-`IsDead` ordering dependency — a future accidental reordering of `Respawn()`'s two key lines must fail this test immediately, not only surface indirectly via Task 4.4's slower PlayMode integration test
- [ ] `Respawn()` called directly while `IsDead` is already `false` is a no-op — no exception, `Respawned` not invoked, position unchanged
- [ ] `Respawn()` teleports to `LastRestedBenchSeat.SitAnchor.position` when a bench has been rested at
- [ ] `Respawn()` teleports to the captured `_spawnPosition` fallback when `LastRestedBenchSeat` is `null`
- [ ] `Respawn()` zeroes `_rigidbody.velocity` and forces `IsGrounded` `true` regardless of pre-death values
- [ ] After `Respawn()`, a chained integration test proves normal input works again: `Die()` → `AdvanceRespawnTimer` past the delay → `TryJump(true)` returns `true`
- [ ] Full pre-existing `PlayerControllerTests` suite passes unmodified — **regression check**
- [ ] `AnimatorContractTests.cs` full suite re-run and confirmed still green (aside from Task 4.2's own additive assertions) — **regression check**

---

**Phase 1 dependency graph:**
```
1.1 [GAMEPLAY] bench tracking + spawn fallback ──→ 1.2 [GAMEPLAY] Respawn()/timer/event
(strictly sequential, same file; both parallel with Phase 2's Task 2.1, a different asset)
```

---

## Phase 2 — `[GAMEPLAY]` Animator Controller change

#### Task 2.1: `[GAMEPLAY]` `RespawnTrigger` parameter + `Die → Idle` transition
**Depends on:** none
**Parallel:** yes — with all of Phase 1 (different asset)
**Touches:** `Assets/Animations/Quirrel.controller` — adds one new Trigger parameter; adds one new outgoing transition on the `Die` state
**Regression risk:** a known silent-breakage vector per `CLAUDE.md`'s regression checklist. Must be performed via the live Unity Editor MCP tools, not a hand-edited YAML patch — and per this project's own Agent Learning, the change must be **re-verified on disk after at least one subsequent domain reload/compile**, not trusted from a single save.

Implementation: add `RespawnTrigger` (type Trigger) to the controller's parameter list. Add exactly one new outgoing transition on `Die` (currently `m_Transitions: []`) → `Idle`, condition `RespawnTrigger`, `HasExitTime: 0`, `TransitionDuration: 0`, `CanTransitionToSelf: 0`.

**Acceptance criteria:**
- [ ] Live Unity Editor check: the controller now has 14 parameters (13 existing + `RespawnTrigger`), `RespawnTrigger` is type Trigger
- [ ] Live Unity Editor check: `Die` has exactly 1 outgoing transition, to `Idle`, condition `RespawnTrigger`, `HasExitTime: 0`
- [ ] Live Unity Editor check, **re-run after at least one subsequent compile/domain reload**: the transition is still present on disk
- [ ] Every other existing state's outgoing-transition count/order confirmed byte-identical to pre-task state — **regression check**
- [ ] The existing `AnyState` transitions (`DieTrigger`, `HurtTrigger`, `AttackTrigger`) confirmed unchanged — **regression check**

---

**Phase 2 dependency graph:**
```
2.1 [GAMEPLAY] RespawnTrigger + Die→Idle transition (independent, parallel with all of Phase 1)
```

---

## Phase 3 — `[GAMEPLAY]` `PlayerHealth.cs` — heal-on-respawn wiring

#### Task 3.1: `[GAMEPLAY]` Subscribe `Respawned → FullHeal`
**Depends on:** Task 1.2 (needs the real `Respawned` event to exist and compile against)
**Parallel:** yes — with Task 2.1. Not parallel with Task 1.2 (compile dependency)
**Touches:** `Assets/Scripts/Player/PlayerHealth.cs` — `OnEnable`/`OnDisable` only, two new lines
**Regression risk:** `OnEnable`/`OnDisable` already subscribe/unsubscribe `Rested += FullHeal` (plan 010). The new subscription must be added alongside it, not in place of it.

**Acceptance criteria:**
- [ ] `OnEnable` subscribes `_playerController.Respawned += FullHeal` (defensive null-check, matching the existing `Rested` subscription's style exactly)
- [ ] `OnDisable` unsubscribes it
- [ ] Invoking `PlayerController.Respawned` on a damaged `PlayerHealth` restores `CurrentQuarterMasks` to `MaxQuarterMasks` — new test
- [ ] Disabling `PlayerHealth` then invoking `Respawned` again does not throw and does not heal — new test
- [ ] All pre-existing `Rested`-related `PlayerHealthTests` pass unmodified — **regression check**

---

**Phase 3 dependency graph:**
```
1.2 [GAMEPLAY] Respawned event ──→ 3.1 [GAMEPLAY] PlayerHealth subscribes Respawned → FullHeal
(3.1 also parallel with 2.1)
```

---

## Phase 4 — `[QA]` Tests and manual playtest addendum

#### Task 4.1: `[QA]` `PlayerControllerTests.cs` — EditMode coverage for Tasks 1.1/1.2
**Depends on:** Task 1.1, Task 1.2
**Parallel:** yes — with Task 4.2, Task 4.3
**Touches:** `Assets/Scripts/Player/Tests/EditMode/PlayerControllerTests.cs`
**Regression risk:** additive only.

**Acceptance criteria:**
- [ ] All acceptance criteria from Tasks 1.1/1.2 each backed by at least one passing test
- [ ] No test calls `Input.GetKey`/`GetKeyDown` directly, consistent with this file's existing convention
- [ ] Full suite passes

---

#### Task 4.2: `[QA]` `AnimatorContractTests.cs` — `RespawnTrigger` + `Die → Idle` contract
**Depends on:** Task 1.2 (needs `RespawnTriggerHash` to exist for the reflection-based hash check), Task 2.1 (needs the real controller-side change)
**Parallel:** yes — with Task 4.1, Task 4.3
**Touches:** `Assets/Scripts/Player/Tests/EditMode/AnimatorContractTests.cs` — adds `RespawnTrigger` as a 14th row to the existing `ExpectedParameters` table; adds a new `DieState_HasExactlyOneOutgoingTransition_ToIdle` test using the file's existing `FindStateByName`/`AssertTransition` helpers; **renames the existing, now-doubly-stale `Controller_HasExactly10Parameters_MatchingPlayerControllerContract` test method** to `Controller_ParameterCount_MatchesPlayerControllerContract` — cosmetic only (confirmed via grep: no other reference to the old name anywhere in the repo), its assertion body already uses `ExpectedParameters.Length` dynamically, not a hardcoded literal, so this is a rename, not a behavior change
**Regression risk:** additive; the parameter-count assertion is driven by `ExpectedParameters.Length`, so adding a row updates the check automatically.

**Acceptance criteria:**
- [ ] `ExpectedParameters` gains `("RespawnTriggerHash", "RespawnTrigger", AnimatorControllerParameterType.Trigger)`; the existing parameter-count test (under its corrected name) passes against the updated table
- [ ] New test: `Die`'s single outgoing transition asserted with destination `Idle` and condition `(AnimatorConditionMode.If, "RespawnTrigger", 0f)`
- [ ] The stale test method name is corrected; no other change to that test's body or assertions
- [ ] Every other existing test in this file re-run and confirmed still green — **regression check**

---

#### Task 4.3: `[QA]` `PlayerHealthTests.cs` — coverage for Task 3.1
**Depends on:** Task 3.1
**Parallel:** yes — with Task 4.1, Task 4.2
**Touches:** `Assets/Scripts/Player/Tests/EditMode/PlayerHealthTests.cs`
**Regression risk:** additive only.

**Acceptance criteria:**
- [ ] All acceptance criteria from Task 3.1 each backed by at least one passing test
- [ ] Full suite passes

---

#### Task 4.4: `[QA]` `PlayerRespawnPlayModeTests.cs` — real-physics/timing end-to-end proof
**Depends on:** Task 1.2, Task 2.1, Task 3.1 (needs the whole feature, including the real Animator asset, for a genuine end-to-end check)
**Parallel:** yes — with Task 4.1, Task 4.2, Task 4.3
**Touches:** new `Assets/Scripts/Player/Tests/PlayMode/PlayerRespawnPlayModeTests.cs`
**Regression risk:** none (new file).

Implementation: mirrors `PlayerHealthPlayModeTests.cs`'s rig-in-`[UnitySetUp]` convention, but — unlike that suite's Animator-less rig (confirmed by the reviewer to have no Animator attached) — this one also attaches a real `Animator` with `Quirrel.controller` assigned, specifically to prove `RespawnTrigger` drives the real asset end-to-end. Adds a minimal `TestBenchSeat` stub (a bare `GameObject` + child anchor `Transform` implementing `IBenchSeat` directly — confirmed sufficient by the reviewer, since `Respawn()` only ever reads `SitAnchor` and never calls `TrySit`/`SetVisible`) positioned away from spawn.

**Acceptance criteria:**
- [ ] Full cycle under real Play Mode timing: `TrySit(true)` with `NearBench` set to the `TestBenchSeat` stub → walk away → `PlayerHealth.ApplyDamage(16)` (instant fatal hit) → wait `_respawnDelay + 0.1s` → confirm `_rigidbody.position` equals the stub's `SitAnchor.position`, `IsDead` is `false`, `CurrentQuarterMasks == MaxQuarterMasks` (via the live `Respawned → FullHeal` event), and the live Animator's current state (layer 0) `IsName("Idle")`
- [ ] Waiting less than `_respawnDelay` after death: `IsDead` still `true`, position unchanged, Animator still in `Die`
- [ ] Post-respawn, `TryJump` succeeds under real Play Mode physics — confirms no residual guard-flag/physics state survived the cycle
- [ ] A second full death→respawn cycle in the same test succeeds identically — proves the timer/Animator transition are safely re-triggerable, not one-shot
- [ ] All existing `PlayerControllerPlayModeTests.cs`/`PlayerHealthPlayModeTests.cs` tests pass unmodified — **regression check**

---

#### Task 4.5: `[QA]` Manual playtest protocol addendum — new `5g` section
**Depends on:** Task 4.1, 4.2, 4.3, 4.4
**Parallel:** no
**Touches:** `Docs/Plans/002_manual-playtest-protocol.md` — new `## 5g. Death and respawn` section, following `5f`'s own heading convention, inserted before `## 6. Sign-off`
**Regression risk:** none to code — documentation only.

New `5g` rows: dying via plan 010's existing QA `TestDamageHazard` (16 cumulative hits, spaced past its 1s invulnerability window each time) plays the Die animation, then after `_respawnDelay` seconds automatically repositions Quirrel standing at the Bench with a full 4-pip Mask HUD, no button press required; Quirrel is **not** auto-seated after respawn (Decision 4), confirmed as intended; movement/jump/attack/sit all work normally immediately after respawn; walking away from the Bench, resting again, then dying again respawns at the Bench again (not the walked-away position), confirming `LastRestedBenchSeat` persists correctly across a stand-up; dying near the far-map QA hazard does not respawn the player back inside the hazard's trigger; regression re-check of `5a`–`5f`.

**Acceptance criteria:**
- [ ] New `5g` section added with all rows above, following the `5a`–`5f` format exactly
- [ ] Live verification performed and recorded PASS/FAIL for every new `5g` row
- [ ] Any FAIL logged as a new bug per this doc's existing bug-report format, routed back through the pipeline, not silently patched inside this task
- [ ] `5a`–`5f` sections re-confirmed to still read correctly — **regression check**

---

**Phase 4 dependency graph:**
```
1.1, 1.2 ──→ 4.1 [QA] PlayerController tests ───────────────┐
1.2, 2.1 ──→ 4.2 [QA] Animator contract tests ───────────────┤
3.1 ────────→ 4.3 [QA] PlayerHealth tests ────────────────────┼──→ 4.5 [QA] manual playtest addendum
1.2, 2.1, 3.1 → 4.4 [QA] PlayMode end-to-end respawn tests ──┘
(4.1–4.4 mutually parallel — different files/assemblies; 4.5 needs the whole feature + all green suites)
```

---

## Explicitly out of scope for this plan

Persistence of `LastRestedBenchSeat` across a scene reload or save file; a multi-bench selection/priority system; any "You Died" screen, death counter, or new UI; a "press any key to respawn" prompt; Shade/geo-drop-and-recovery on death; any new QA test hazard; any `ProjectSettings`/tag/physics-layer/collision-matrix change; any change to `Bench.cs` or `IBenchSeat`; any `SampleScene.unity` edits; fixing the pre-existing `IBenchSeat`-interface "fake null" caveat at its other, pre-existing call sites (Decision 12 — named and accepted, not fixed, at both the pre-existing and this plan's new call site).

---

## Judgment calls made explicit

1. Respawn is a pure in-memory, same-scene teleport — no save/persistence layer (Decision 1).
2. "Last bench rested at" is a plain reference field, deliberately scoped to today's single-bench reality (Decision 2).
3. Respawn positions Quirrel standing at the bench, not auto-seated (Decision 4).
4. Fixed delay, not player-input-gated (Decision 5).
5. `PlayerController` gains exactly one new event (`Respawned`), mirroring `Rested`'s already-established pattern (Decision 7).
6. The guard-flag trace found only two genuinely-needed respawn-time resets (`IsGrounded`, Rigidbody2D velocity) — independently re-verified and found *more* robust than originally described (Decision 8); `Die()`'s own missing re-entrancy guard is named as a separate, accepted, currently-unreachable edge case.
7. The Animator change is scoped to a `Die`-only transition, not `AnyState` (Decision 10).
8. Decision 12 names the specific `MissingReferenceException` crash a destroyed-but-fake-non-null Bench would cause via the interface-typed `LastRestedBenchSeat`, accepted as unreachable today since nothing in this codebase ever destroys a `Bench`, with an explicit forward-pointer for any future plan that changes that.

---

## Reference file paths consulted while drafting this plan

- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\CLAUDE.md`
- `Docs\Plans\010_health-mask-system.md`, `008_bench-sit-mechanic.md`, `009_bench-visual-fixes.md`, `002_manual-playtest-protocol.md`
- `Assets\Scripts\Player\PlayerController.cs`, `Assets\Scripts\Player\PlayerHealth.cs`
- `Assets\Scripts\Environment\Bench.cs`, `Assets\Scripts\Player\IBenchSeat.cs`, `Assets\Scripts\Environment\TestDamageHazard.cs`
- `Assets\Animations\Quirrel.controller`, `Assets\Animations\Clips\Quirrel_Die.anim`
- `Assets\Scenes\SampleScene.unity`
- `Assets\Scripts\Player\Tests\EditMode\PlayerControllerTests.cs`, `AnimatorContractTests.cs`, `PlayerHealthTests.cs`
- `Assets\Scripts\Player\Tests\PlayMode\PlayerHealthPlayModeTests.cs`, `PlayerControllerPlayModeTests.cs`

---

**Approved for implementation.** Per this repo's pipeline, this plan file is committed and pushed before any implementation agent begins work on it.
