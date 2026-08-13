# Implementation Plan — Quirrel's Health/Mask System

**Status:** ✅ APPROVED (Revision 2, accepted with minor revisions by implementation-plan-reviewer)
**Author:** implementation-plan-architect
**Date:** 2026-08-13
**Feature:** A Mask-based health system for Quirrel: 4 starting Masks, up to 9 max via Mask Shards (4 shards = +1 max Mask; shards raise the ceiling only, never heal directly), a full-heal Mask pickup, damage from a generic public API any future enemy/hazard can call, healing from resting on a Bench (Focus/Soul explicitly deferred), a HUD showing the Mask row in the upper-left, and a **temporary, QA-only** test hazard wall at the right-most edge of the current map dealing exactly 1 quarter-mask of chip damage to prove the underlying model supports sub-Mask granularity.

**Revision history:**
- **Revision 1** (draft): initial plan from implementation-plan-architect.
- **Revision 2** (this version): addressed implementation-plan-reviewer's NEEDS REVISION verdict on Revision 1 — added per-phase dependency graphs; brought Phase 3 up to full task-formatting parity with every other phase; fixed a missing `UnityEngine.UI` asmdef reference that would have failed to compile; replaced a PlayMode/live invulnerability check that passed trivially regardless of whether the window worked with one that actually isolates its effect (exit-then-rapid-re-entry-within-window vs. exit-then-re-entry-after-window-elapsed); changed `MaxMasksCap` from a serialized tunable to a shared `const` to close a silent HUD-pip-mismatch risk; made explicit which assembly Phase 2's new scripts/tests live in and added `isTrigger` defensive checks/regression criteria to the pickups; split the original Task 4.1 into two smaller tasks to keep both under the 1–4h budget.
- **Reviewer's final pass** on Revision 2: **ACCEPT WITH MINOR REVISIONS** — all 6 blocking/major findings from the first round confirmed fixed against the actual source (asmdef reference conventions, `Bench.Awake()`'s real pattern, `TrySit`'s single success path, `Quirrel.Environment.EditModeTests.asmdef`'s existence). Three minor items remained, folded directly into this final version:
  1. `Quirrel.UI.asmdef`'s `references` must explicitly retain **both** `"Quirrel.Player"` and `"UnityEngine.UI"` — stated explicitly in Task 4.3 below.
  2. The pickups' defensive `isTrigger` check should generalize via `GetComponent<Collider2D>()` rather than literally copying `Bench.Awake()`'s `BoxCollider2D`-specific check (confirmed by reading `Bench.cs`: its check is hardcoded to `BoxCollider2D`) — since the pickups' final collider shape isn't fixed by this plan, Tasks 2.1/2.2 below use the generalized form of the same pattern (get the collider, check `.isTrigger`, `Debug.LogError` with `this` context) rather than the narrower literal copy.
  3. Phase 3's task formatting (full `### Task N.N: [DISCIPLINE] Name` / `Depends on:` / `Parallel:` / `Touches:` structure) is confirmed present in this final document — see Phase 3 below.

---

## 0. Summary and verified blast radius

**Confirmed from the actual source (read directly, not assumed, independently re-verified by implementation-plan-reviewer):**

- `Assets/Scripts/Player/PlayerController.cs` (1015 lines). `Hurt()` and `Die()` are already public, already fully implemented, and are explicitly documented as forward-hooks for "an external combat/health system" that doesn't exist yet. Both already: force-clear every derived-input guard flag (`_jumpInProgress`, `_isAttacking`, `DefendHeld`, `LookingUp`/`LookingDown`), call the existing `StandUp()` helper (so a seated character is stood up automatically), and drive the Animator's `HurtTrigger`/`DieTrigger`/`IsDead` bool. `Hurt()` no-ops entirely if `IsDead` is already true; `Die()` has no such guard (idempotent by construction). Neither method currently has any caller anywhere in the codebase.
- `IsDead` is `public bool IsDead { get; set; }` — already the exact shape needed for an external system to read.
- `TrySit(bool isNearBench)`'s success path (the branch that sets `_isSitting = true`) is a single, already-isolated insertion point, confirmed by direct read (lines 887–918): there is exactly one `return true;`, immediately after `_isSitting = true` and the animator/bench-snap logic, with every early-exit returning `false` beforehand.
- `Assets/Animations/Quirrel.controller`: `HurtTrigger`, `HurtRecoveryTrigger`, `DieTrigger`, `IsDead` are already-wired parameters. The `Hurt` state (fileID 1102009) and `Die` state (fileID 1102010) already have real Motion clips assigned, not empty placeholders — this plan requires **zero Animator changes**. `Die`'s `m_Transitions: []` confirms no reset path exists (dying is a permanent dead-end today).
- `Assets/Scripts/Environment/Bench.cs` / `Assets/Scripts/Player/IBenchSeat.cs`: the established pattern for a trigger-zone environment prop that calls into `PlayerController` via `GetComponent<T>()` on whatever collider enters — `OnTriggerEnter2D`/`OnTriggerExit2D` forward to public, parameter-driven `HandleTriggerEnter`/`HandleTriggerExit` methods for EditMode testability. `Bench.Awake()` also has an explicit `Debug.LogError` guard for a misconfigured non-trigger collider, hardcoded to `BoxCollider2D` specifically — an already-institutionalized regression-prevention pattern this plan's new pickups replicate in generalized form (see the Revision 2 note above).
- `Assets/Scripts/Player/Quirrel.Player.asmdef` references `[]`; `Assets/Scripts/Environment/Quirrel.Environment.asmdef` references `["Quirrel.Player"]`. The new `PlayerHealth` component lives inside the existing `Quirrel.Player` assembly (not a new assembly), so `Quirrel.Environment` (pickups, hazard) can call it via its already-existing reference, and the new `Quirrel.UI` assembly (Task 4.3) references `Quirrel.Player` by the same precedent — no asmdef graph restructuring needed anywhere.
- `Assets/Scenes/SampleScene.unity`: `Ground` (fileID `1036796224`) sits at `{x: 0, y: -0.5, z: 1}`, `localScale {x: 30, y: 1, z: 1}`, `BoxCollider2D` `m_Offset: {0,0}` / `m_Size: {1,1}` → world collider spans **x: −15 to +15**, top surface at world **y = 0**. **The right-most edge of the current map is therefore world x = 15** — to be **live-confirmed** in the Unity Editor, not trusted from arithmetic alone. `Player` (fileID `563516957`) spawns at `{0,0,0}`, `BoxCollider2D` `m_Offset: {0, 0.655}` / `m_Size: {1, 1.31}`. **No `Canvas` GameObject exists anywhere in this scene** (confirmed by grep) — this plan's HUD is a from-scratch UI addition.
- `Assets/Sprites/Reference/health.png`: 52×58px, RGBA — a single Mask icon reference image, no filled/empty variant pair supplied.
- `ART.md`: §2.2 defines "Mask, healthy" = `#F2EEE3` fill / "Mask ink line" = `#1B1B1F` — directly reusable for the HUD's filled pip. §2.5/§4.1 describe a different, not-yet-built diegetic mask-crack health system, explicitly contrasted against "a floating HP bar." §9 confirms this crack art doesn't exist yet. §2.4 has no existing "empty" Mask pip token. §7 specifies **1920×1080 reference resolution, Canvas Scaler "Scale With Screen Size,"** flagged as needing ui-developer coordination "if UI work begins" — it begins here.
- No enemy AI, no Soul/Focus/Hot Spring system, no save/persistence system, and no checkpoint/respawn system exists anywhere in the codebase (confirmed by glob/grep, independently re-confirmed with zero hits by implementation-plan-reviewer).

### 0.1 Open design tension — reviewed, confirmed non-blocking

`ART.md` §4.1 states the mask "is the primary health-feedback surface... this is diegetic UI, not a floating HP bar." This plan implements exactly a floating-HP-bar-shaped HUD (a row of Mask icons, upper-left) because that is both (a) literally what the user's feature spec asked for, with a concrete reference image supplied, and (b) the real Hollow Knight/Silksong convention. The two systems are additive, not mutually exclusive — §2.5's diegetic crack system remains a legitimate, still-unbuilt future enhancement layered on top of, not replacing, this HUD. Task 4.2 appends one factual note to `ART.md` §9 recording this deferral without rewriting §4.1's stated design philosophy. **implementation-plan-reviewer independently confirmed this reading is reasonable and does not need to block implementation.**

**Complete write list for this plan:**

| Path | Written by | Why |
|---|---|---|
| `Assets/Scripts/Player/PlayerController.cs` | Task 1.1 | +1 public event (`Rested`), +1 invocation line inside `TrySit`'s existing success branch |
| `Assets/Scripts/Player/PlayerHealth.cs` (new, `Quirrel.Player` assembly) | Task 1.2, 1.3 | Core health data model, damage/death path, heal/shard API, invulnerability window |
| `Assets/Scripts/Environment/MaskShardPickup.cs` (new, `Quirrel.Environment` assembly) | Task 2.1 | New pickup |
| `Assets/Scripts/Environment/MaskPickup.cs` (new, `Quirrel.Environment` assembly) | Task 2.2 | New pickup |
| `Assets/Scripts/Environment/TestDamageHazard.cs` (new, `Quirrel.Environment` assembly) | Task 2.3 | **Temporary QA/dev aid** |
| `Assets/Scenes/SampleScene.unity` | Task 3.1, 3.2, 3.3, 4.4 | +`PlayerHealth` on `Player`; +hazard wall; +test pickups; +Canvas/HUD hierarchy |
| `Assets/Sprites/Reference/health.png` (read-only source) | — | Not modified |
| `Assets/Sprites/UI/Mask/Mask_Filled.png` (+`.meta`, new) | Task 4.1 | HUD pip sprite, filled |
| `Assets/Sprites/UI/Mask/Mask_Empty.png` (+`.meta`, new) | Task 4.2 | HUD pip sprite, empty |
| `ART.md` §2.4, §9 | Task 4.2 | +1 HUD color-token row pair; +1 deferral note |
| `Assets/Scripts/UI/Quirrel.UI.asmdef` (new) | Task 4.3 | New assembly, references `Quirrel.Player` + `UnityEngine.UI` |
| `Assets/Scripts/UI/MaskHUD.cs` (new) | Task 4.3 | HUD render logic |
| `Assets/Scripts/Player/Tests/EditMode/PlayerHealthTests.cs` (new) | Task 5.1 | New tests |
| `Assets/Scripts/Player/Tests/EditMode/PlayerControllerTests.cs` | Task 5.3 | New tests for `Rested` event |
| `Assets/Scripts/Environment/Tests/EditMode/MaskShardPickupTests.cs`, `MaskPickupTests.cs`, `TestDamageHazardTests.cs` (new, `Quirrel.Environment.EditModeTests` assembly) | Task 5.2 | New tests |
| `Assets/Scripts/Player/Tests/PlayMode/PlayerHealthPlayModeTests.cs` (new) | Task 5.4 | New tests |
| `Assets/Scripts/UI/Tests/EditMode/Quirrel.UI.EditModeTests.asmdef`, `MaskHUDTests.cs` (new) | Task 5.5 | New assembly + tests |
| `Docs/Plans/002_manual-playtest-protocol.md` | Task 5.6 | New `5f` section |

**Confirmed out of scope:** enemy AI of any kind (no enemy system exists — `ApplyDamage(int)` is the generic future hook); Soul/Focus/self-heal ability and any Soul-meter UI (§2.4's reserved `#BFE3FF` token stays reserved, unused); Hot Spring prop; save/load or persistence of any Mask/shard state (no save system exists — a future plan defines this); checkpoint/respawn on death (dying remains a permanent dead-end state, a pre-existing gap this plan does not fix); any `ProjectSettings`/tags/physics-layers/collision-matrix change; any Animator Controller change (Hurt/Die already have real Motion clips wired); partial/"cracked" pip HUD visual; `ART.md` §2.5's diegetic system (deferred, not built, not removed); a visual invulnerability flicker/flash effect; real, hand-placed shippable level content beyond a small number of QA-verification placements; an Input Actions migration.

**Metroidvania-specific check:** Mask Shards and the Mask pickup are the first progression-relevant unlock this codebase has — `MaxMasks` growing via shards is real player-facing progression, but since no save system exists yet, it has no persisted ID and does not survive a scene reload; a future save-system plan must assign it one. No new traversal ability is granted. No sequence-break risk. Checkpoint integrity: dying is a pre-existing dead-end — QA's manual pass (Task 5.6) must exercise dying deliberately and confirm it fails safely (no exception, no corrupted state), not that it's recoverable.

---

## 1. Design decisions

**1. `PlayerHealth` is a new sibling `MonoBehaviour` on the Player GameObject, inside the existing `Quirrel.Player` assembly** — not merged into `PlayerController`, not a new assembly. Rejected alternative: fold health fields into `PlayerController` directly — rejected given that file's already-large regression surface.

**2. Internal health storage is `int` quarter-masks, not `float` masks.** Standard hit = `QuartersPerMask` (4) quarter-masks; QA test hazard = 1.

**3. Mask Shards increase `MaxMasks` only, never current health** — the user's explicit brief instruction. `AddMaskShard()` increments shard-progress; at `ShardsPerMask` (4), resets to 0 and raises `MaxMasks` by 1 (capped at `MaxMasksCap`) — `CurrentQuarterMasks` untouched.

**4. `MaskPickup` is a full heal** (`FullHeal()` → `CurrentQuarterMasks = MaxQuarterMasks`), entirely distinct from a Mask Shard.

**5. `ApplyDamage(int quarterMasks)` is a single generic public API**, not a fixed "standard hit" method — mirrored by `Heal(int)` on the regen side, so both sides are ready for a future enemy/Focus system to call without modification.

**6. A new damage-invulnerability window (`_damageInvulnerabilityDuration`, default 1.0s, tunable) is added to `PlayerHealth`, separate from `PlayerController`'s existing 0.3s hit-stun — honest rationale.** `TestDamageHazard` fires only on `OnTriggerEnter2D`, never `OnTriggerStay2D` (Decision 11) — Unity will not re-fire enter for a stationary player, so a player standing inside the persistent test-hazard wall draining health "far faster than one chip per touch" is already fully prevented by Enter-only firing alone, independent of any invulnerability window. The window's real, honest justification is **forward-looking i-frame infrastructure**: the genre-standard mechanism (near-universal in Hollow Knight and this project's other reference points) for protecting against a hit-then-immediately-hit-again scenario — the kind of rapid re-entry a player bumping in and out at a hazard's boundary can trigger today, and the kind of continuous-overlap or `OnTriggerStay2D`-based hazard/enemy a future plan will certainly add. It is cheap to add now, expected by the genre, and this plan's own test hazard is the right low-stakes place to prove the mechanism works before anything higher-stakes depends on it — but it is not something today's literal QA hazard strictly requires on its own. `ApplyDamage` no-ops entirely while `IsInvulnerable` is true (mirrors `Hurt()`'s no-op-while-`IsDead` convention). Window starts only on a non-fatal hit. Ticked by `AdvanceInvulnerabilityTimer(float deltaTime)`, same shape as `PlayerController.AdvanceHurtStunTimer`.

**7. `MaxMasksCap` is a `public const int MaxMasksCap = 9` on `PlayerHealth`, not a serialized/tunable field.** A serialized tunable here, paired with `MaskHUD`'s hardcoded 9-slot `Image[]` array, would let a designer silently break the HUD by raising the cap above 9 in the Inspector with no error. The user's own spec fixes 9 as the hard design maximum (not a balance knob), so a shared compile-time constant — read by both `PlayerHealth` and `MaskHUD` — removes the drift risk at its source. As belt-and-suspenders (the `_pipSlots` array is still a hand-authored, serialized Inspector reference per Decision 13, and could still be mis-sized by a scene-editing mistake independent of the cap), `MaskHUD.OnEnable`/`Start` also asserts `_pipSlots.Length == PlayerHealth.MaxMasksCap` and logs a loud `Debug.LogError` if mismatched, matching this codebase's established "catch a misconfiguration loudly" convention (`Bench.Awake()`'s `isTrigger` check).

**8. The HUD renders exactly 2 pip states — filled and empty — with no partial/"cracked" pip art.** `filledPipCount = CurrentQuarterMasks / QuartersPerMask` (integer division, rounds down). **Named, accepted limitation:** the QA hazard's 1-quarter-mask chip damage isn't independently visible as a distinct partial-pip state on the HUD — accepted as genre-accurate (the real reference game's vanilla pip HUD also has only 2 states per pip) and because no *real* content in this plan ever deals sub-Mask damage.

**9. `PlayerController` gains exactly one new public event, `Rested`**, invoked at the single existing point inside `TrySit()` where `_isSitting = true` is set on success — no other line touched. `PlayerHealth` subscribes `Rested += FullHeal` in `OnEnable` (unsubscribes `OnDisable`). First event/delegate used anywhere in this codebase. Rejected alternative: poll `IsSitting` for a false→true edge — rejected as needlessly stateful. Fires harmlessly on the pre-existing spawn-time auto-sit path too (`FullHeal()` on an already-full character is a no-op by construction, Decision 10), verified by an explicit test.

**10. `Heal(int)` and `FullHeal()` are idempotent no-ops** when there's nothing to heal or the character is dead, mirroring `Hurt()`'s convention. `FullHeal() = Heal(MaxQuarterMasks)`, not a duplicated code path.

**11. `TestDamageHazard` fires on `OnTriggerEnter2D` only (never `OnTriggerStay2D`)**, matching `Bench`'s single-fire-per-edge convention. This is the *primary* mechanism preventing rapid in-place health drain; Decision 6's invulnerability window is deliberate, additional, forward-looking defense-in-depth on top of it, not a redundant restatement of it.

**12. The test hazard is a code-only, deliberately invisible `MonoBehaviour`** — no `SpriteRenderer`, no `[ART]` task. Its class-level doc comment states plainly it is a temporary QA/dev aid, not shippable content, naming this plan for removal reference. An `OnDrawGizmos` wire-cube gives Editor-only placement convenience with zero runtime visual footprint.

**13. `MaskHUD`'s 9 pip slots are pre-authored `Image` GameObjects in the scene hierarchy** (serialized `Image[]` array), not runtime-`Instantiate`d from a prefab — matches `Bench._sitAnchor`'s existing "assign in Inspector, defensive fallback/assert if misconfigured" convention.

**14. `MaskShardPickup`/`MaskPickup`/`TestDamageHazard` all live in the existing `Quirrel.Environment` assembly** — the same assembly `Bench.cs` already lives in, requiring zero new asmdef. Their EditMode tests (Task 5.2) land in the existing `Assets/Scripts/Environment/Tests/EditMode/Quirrel.Environment.EditModeTests.asmdef` (confirmed present on disk, referencing `["Quirrel.Environment", "Quirrel.Player", "UnityEngine.TestRunner", "UnityEditor.TestRunner"]`, alongside `BenchTests.cs`) — no new test assembly needed for Phase 2's coverage.

**15. `MaskShardPickup`/`MaskPickup` each get an `Awake()` defensive `isTrigger` check following the same pattern as `Bench.Awake()`'s existing `Debug.LogError` guard, generalized to `GetComponent<Collider2D>()` rather than `Bench`'s literal `BoxCollider2D`-specific check** (confirmed by reading `Bench.cs`: its check is hardcoded to `BoxCollider2D`). Since this plan doesn't fix the pickups' final collider shape (a rounded pickup sprite might reasonably use `CircleCollider2D`), the generalized form — get the `Collider2D`, check `.isTrigger`, `Debug.LogError` with `this` context if false — applies regardless of concrete collider type, while still following the exact same institutionalized pattern `Bench` already established. A misconfigured non-trigger pickup collider would physically block the player exactly like a misconfigured Bench would; this codebase has already institutionalized loudly-logging this exact misconfiguration class once, and the new pickups follow the same precedent rather than silently omitting it.

---

## Phase 1 — `[GAMEPLAY]` Health data model and core damage/heal path

#### Task 1.1: `[GAMEPLAY]` `PlayerController.cs` — add the `Rested` event
**Depends on:** none
**Parallel:** yes — with Task 1.2 (different file)
**Touches:** `Assets/Scripts/Player/PlayerController.cs` (adds one public event declaration and one invocation line inside `TrySit`'s existing success branch; changes nothing else)
**Regression risk:** `TrySit` is existing, heavily-tested, order-sensitive (plans 008/009). Must add exactly one line at exactly one point (immediately after the existing `_isSitting = true;`/Animator-bool-set lines, before the `_seatedBench` snap-and-hide block).

**Acceptance criteria:**
- [ ] `public event System.Action Rested;` declared; `Rested?.Invoke();` called exactly once, only on `TrySit`'s success path
- [ ] A failed `TrySit` call (any existing early-return guard) does not invoke `Rested` — new test
- [ ] Full pre-existing `PlayerControllerTests` suite (plans 002–009) passes unmodified — **regression check**
- [ ] `AnimatorContractTests.cs` full suite re-run and confirmed still green — **regression check**

---

#### Task 1.2: `[GAMEPLAY]` `PlayerHealth.cs` — data model, damage/death path, invulnerability
**Depends on:** none
**Parallel:** yes — with Task 1.1. **Not** parallel with Task 1.3 (same new file)
**Touches:** none existing — creates `Assets/Scripts/Player/PlayerHealth.cs` (`Quirrel.Player` assembly)
**Regression risk:** calls into `PlayerController.Hurt()`/`Die()` — must invoke, never reimplement, their existing contracts.

Implementation: `[RequireComponent(typeof(PlayerController))]`. Idempotent `EnsureCachedComponents()`. Serialized tunables: `_startingMasks = 4`, `_shardsPerMask = 4`, `_damageInvulnerabilityDuration = 1.0f`. `public const int MaxMasksCap = 9;` (Decision 7 — compile-time constant, not serialized) and `public const int QuartersPerMask = 4;` (kept as a separately-named unit constant from `_shardsPerMask` even though both happen to be 4, so a future design change to one never silently drags the other along). Private fields `_maxMasks`, `_currentQuarterMasks`, `_maskShardProgress`, `_isInvulnerable`, `_invulnerabilityTimeRemaining`, initialized in `Awake()`. Public properties `MaxMasks`, `CurrentQuarterMasks`, `MaxQuarterMasks`, `MaskShardProgress`, `ShardsPerMask`, `IsInvulnerable`. `event System.Action OnHealthChanged`. `ApplyDamage(int quarterMasks)`: no-ops if `IsDead`/`IsInvulnerable`/`quarterMasks <= 0`; else clamps `CurrentQuarterMasks -= amount` (floor 0), raises `OnHealthChanged`, calls `Die()` if now 0 else `Hurt()` + starts invulnerability window; returns bool. `AdvanceInvulnerabilityTimer(float deltaTime)` ticks the window down, same shape as `AdvanceHurtStunTimer`. `FixedUpdate` calls it.

**Acceptance criteria:**
- [ ] Fresh instance: `MaxMasks == 4`, `CurrentQuarterMasks == 16`, `MaskShardProgress == 0`, `IsInvulnerable == false`
- [ ] `ApplyDamage(4)`: `CurrentQuarterMasks == 12`, `IsInvulnerable` becomes true, `Hurt()` called exactly once, `Die()` not called
- [ ] `ApplyDamage(1)` (QA hazard's amount): `CurrentQuarterMasks == 15`
- [ ] `ApplyDamage` reducing to exactly 0: `Die()` called, `IsDead` true, `IsInvulnerable` stays false
- [ ] Second `ApplyDamage` call while `IsInvulnerable` is true returns false, health unchanged
- [ ] `AdvanceInvulnerabilityTimer` clears `IsInvulnerable` exactly once the window elapses; a subsequent `ApplyDamage` succeeds again
- [ ] `ApplyDamage` with `IsDead` already true returns false, no throw
- [ ] `ApplyDamage(0)` or negative is a no-op, returns false
- [ ] `OnHealthChanged` fires exactly once per successful call, zero times on a no-op call
- [ ] `MaxMasksCap` is a `const`, confirmed not serialized/visible as an Inspector tunable

---

#### Task 1.3: `[GAMEPLAY]` `PlayerHealth.cs` — heal API, shard-to-mask conversion, `Rested` subscription
**Depends on:** Task 1.2 (same file), Task 1.1 (needs `Rested` event)
**Parallel:** no
**Touches:** `Assets/Scripts/Player/PlayerHealth.cs` (Task 1.2's file — additive only)
**Regression risk:** none new beyond Task 1.2's own.

Implementation: `Heal(int quarterMasks)`: no-ops if `IsDead`/amount `<= 0`/already at max; else clamps `+= amount` (ceil at max), raises `OnHealthChanged`, returns true. `FullHeal() => Heal(MaxQuarterMasks)`. `AddMaskShard()`: no-ops if `IsDead` or `_maxMasks >= MaxMasksCap`; else increments shard progress, at `_shardsPerMask` resets to 0 and increments `_maxMasks` (capped) — `_currentQuarterMasks` untouched (Decision 3). `OnEnable` subscribes `_playerController.Rested += FullHeal` (defensive null-check); `OnDisable` unsubscribes.

**Acceptance criteria:**
- [ ] `Heal(4)` on 8/16 → 12
- [ ] `Heal(100)` overheal clamps to `MaxQuarterMasks`
- [ ] `Heal` on an already-full character returns false, `OnHealthChanged` fires zero times
- [ ] `FullHeal()` restores exactly to `MaxQuarterMasks`
- [ ] `AddMaskShard()` ×3: `MaskShardProgress == 3`, `MaxMasks` unchanged, `CurrentQuarterMasks` unchanged
- [ ] `AddMaskShard()` 4th call: progress resets to 0, `MaxMasks` becomes 5, `CurrentQuarterMasks` **still** unchanged from before any of the 4 calls — explicit assertion
- [ ] `AddMaskShard()` repeated until `MaxMasks == MaxMasksCap (9)`: further calls return false, unchanged
- [ ] Invoking `PlayerController.Rested` triggers `FullHeal()` end-to-end
- [ ] `OnDisable` correctly unsubscribes — disabling then invoking `Rested` again does not throw
- [ ] All Task 1.2 acceptance criteria still pass unmodified — **regression check**

---

**Phase 1 dependency graph:**
```
1.1 [GAMEPLAY] Rested event ──┐
                                ├──→ 1.3 [GAMEPLAY] Heal/shard API + Rested subscription
1.2 [GAMEPLAY] PlayerHealth core ┘
(1.1 and 1.2 mutually parallel — different files; 1.3 depends on both, same-file-sequential after 1.2)
```

---

## Phase 2 — `[GAMEPLAY]` Environment components (pickups + QA test hazard)

*(All three components live in the existing `Quirrel.Environment` assembly — Decision 14 — no new asmdef.)*

#### Task 2.1: `[GAMEPLAY]` `MaskShardPickup.cs`
**Depends on:** Task 1.3
**Parallel:** yes — with Task 2.2, Task 2.3
**Touches:** none existing — creates `Assets/Scripts/Environment/MaskShardPickup.cs` (`Quirrel.Environment` assembly)
**Regression risk:** none (new file); reuses `Bench`'s `GetComponent<T>()`-on-trigger-enter pattern.

Implementation: `[RequireComponent(typeof(Collider2D))]`. `Awake()` — defensive `isTrigger` check via `GetComponent<Collider2D>()` following `Bench.Awake()`'s pattern, generalized to any collider type (Decision 15): `Debug.LogError` if the collider is not a trigger. `OnTriggerEnter2D` forwards to public `HandleTriggerEnter(Collider2D other)`. Finds `PlayerHealth` on the entering collider → `AddMaskShard()`, then unconditionally destroys the pickup GameObject.

**Acceptance criteria:**
- [ ] Trigger-entering a collider with `PlayerHealth` calls `AddMaskShard()` exactly once, destroys the pickup
- [ ] Trigger-entering a collider with no `PlayerHealth` does not throw, does not destroy the pickup
- [ ] `HandleTriggerEnter` is public, directly callable without a live physics scene
- [ ] A non-trigger `Collider2D` on this component logs a `Debug.LogError` via `Awake()`

---

#### Task 2.2: `[GAMEPLAY]` `MaskPickup.cs`
**Depends on:** Task 1.3
**Parallel:** yes — with Task 2.1, Task 2.3
**Touches:** none existing — creates `Assets/Scripts/Environment/MaskPickup.cs` (`Quirrel.Environment` assembly)
**Regression risk:** none (new file).

Implementation: identical shape to Task 2.1 (including the same generalized `isTrigger` defensive `Awake()` check), calling `FullHeal()` instead of `AddMaskShard()`, then destroying itself unconditionally.

**Acceptance criteria:**
- [ ] Trigger-entering a collider with `PlayerHealth` calls `FullHeal()` exactly once, destroys the pickup
- [ ] Trigger-entering a collider with no `PlayerHealth` does not throw, does not destroy the pickup
- [ ] A non-trigger `Collider2D` logs a `Debug.LogError` via `Awake()`

---

#### Task 2.3: `[GAMEPLAY]` `TestDamageHazard.cs` — **temporary QA/dev aid**
**Depends on:** Task 1.2
**Parallel:** yes — with Task 2.1, Task 2.2
**Touches:** none existing — creates `Assets/Scripts/Environment/TestDamageHazard.cs` (`Quirrel.Environment` assembly)
**Regression risk:** none (new file). Named future risk: this component's own class doc comment must state plainly it is temporary, not shippable content, naming this plan for removal reference.

Implementation: `[RequireComponent(typeof(Collider2D))]`. `OnTriggerEnter2D` only (Decision 11), forwarding to public `HandleTriggerEnter(Collider2D other)`. Finds `PlayerHealth` → `ApplyDamage(1)` (named const `DamageAmountQuarterMasks = 1`, deliberately not `PlayerHealth.QuartersPerMask` — a full-mask hit would defeat this hazard's stated chip-damage purpose). Does not destroy/disable itself — a persistent wall, re-triggerable on repeat entry once invulnerability naturally expires. `OnDrawGizmos` wire-cube, Editor-only, zero runtime visual footprint.

**Acceptance criteria:**
- [ ] Trigger-entering a collider with `PlayerHealth` calls `ApplyDamage(1)` exactly once per entry
- [ ] No `SpriteRenderer` — invisible in Play Mode
- [ ] Class doc comment states "temporary QA/dev aid, not shippable content" and names this plan
- [ ] `OnDrawGizmos` draws a wire cube matching the attached collider's bounds

---

**Phase 2 dependency graph:**
```
1.2, 1.3 [GAMEPLAY] PlayerHealth ──┬──→ 2.1 [GAMEPLAY] MaskShardPickup
                                    ├──→ 2.2 [GAMEPLAY] MaskPickup
                                    └──→ 2.3 [GAMEPLAY] TestDamageHazard (QA-only)
(2.1/2.2/2.3 mutually parallel — different files, all depend only on Phase 1)
```

---

## Phase 3 — `[GAMEPLAY]` Scene wiring, part 1 (Player + hazard + pickups)

**Cross-phase sequencing note:** Tasks 3.1, 3.2, 3.3, and Task 4.4 (Phase 4) all edit `Assets/Scenes/SampleScene.unity` — per this project's "same scene file → never parallel" rule, these four tasks form **one strict linear chain regardless of nominal phase**: 3.1 → 3.2 → 3.3 → 4.4. No other task in this plan touches `SampleScene.unity`.

#### Task 3.1: `[GAMEPLAY]` Add `PlayerHealth` to the `Player` GameObject
**Depends on:** Task 1.3
**Parallel:** no — first link in the cross-phase scene-file chain above
**Touches:** `Assets/Scenes/SampleScene.unity` (`Player` GameObject only — adds one component, touches nothing else)
**Regression risk:** shared scene file. Must not alter `Player`'s existing `Transform`, `Rigidbody2D`, `BoxCollider2D`, `SpriteRenderer`, `Animator`, or `PlayerController` values.

**Acceptance criteria:**
- [ ] `PlayerHealth` component added to `Player`, using default tunable values (4 starting masks / 9 cap / 4 shards-per-mask / 1.0s invuln)
- [ ] Live Unity check: entering Play Mode, `Player.PlayerHealth.MaxMasks == 4` and `CurrentQuarterMasks == 16`
- [ ] `Player`'s existing components' serialized values byte-identical to pre-task state — **regression check**

---

#### Task 3.2: `[GAMEPLAY]` Place `TestDamageHazard` at the live-measured right-most map edge
**Depends on:** Task 2.3, Task 3.1
**Parallel:** no — second link in the chain
**Touches:** `Assets/Scenes/SampleScene.unity` (adds one new GameObject; touches nothing existing)
**Regression risk:** shared scene file. Must not touch `Ground`, `Player`, `Main Camera`, or `Bench`'s existing Transforms/components.

Implementation: per Section 0's confirmed math, `Ground`'s right edge is at world x = 15 (to be **live-confirmed** in Unity, not trusted from arithmetic alone). Create a new GameObject (`QA_TestHazardWall_TEMPORARY`) with a `BoxCollider2D` (`isTrigger = true`) and `TestDamageHazard`, positioned so its left face sits at approximately world x = 15, spanning the Player's full collider height range with generous margin.

**Acceptance criteria:**
- [ ] Live Unity measurement of `Ground`'s actual right-edge world x-coordinate recorded and confirmed to match (or explicitly correct) the computed estimate of 15
- [ ] `TestDamageHazard` GameObject placed such that walking a live Player rightward along `Ground` touches it at the map's actual right-most traversable edge, verified in Play Mode
- [ ] `BoxCollider2D.isTrigger == true`
- [ ] Live Play Mode check: touching the wall once reduces `CurrentQuarterMasks` by exactly 1
- [ ] **Live Play Mode check (the actual proof of the invulnerability window, not merely of Enter-only firing):** walk into the hazard (1 chip taken), immediately step back out and back in again within 1.0s → the second entry is a no-op (`CurrentQuarterMasks` unchanged). Then wait for the window to elapse (~1.0s+) and re-enter → a further chip of damage is taken, confirming the window correctly expires rather than latching permanently.
- [ ] `Ground`, `Player`, `Main Camera`, `Bench` Transforms/components byte-identical to pre-task state — **regression check**

---

#### Task 3.3: `[GAMEPLAY]` Place QA test pickups (4× `MaskShardPickup`, 1× `MaskPickup`)
**Depends on:** Task 2.1, Task 2.2, Task 3.2
**Parallel:** no — third link in the chain
**Touches:** `Assets/Scenes/SampleScene.unity` (adds 5 new GameObjects; touches nothing existing)
**Regression risk:** shared scene file, same discipline as Task 3.2.

Implementation: place 4 `MaskShardPickup` and 1 `MaskPickup` reachable near spawn/the Bench, purely for manual and live QA verification of the pickup flow in this sandbox scene — explicitly a QA/test placement, not real level content.

**Acceptance criteria:**
- [ ] 5 pickups placed and reachable on foot from spawn, no special ability required
- [ ] Live Play Mode check: walking into all 4 `MaskShardPickup`s raises `MaxMasks` from 4 to 5 exactly once
- [ ] Live Play Mode check: walking into the `MaskPickup` after taking damage restores `CurrentQuarterMasks` to current `MaxQuarterMasks`
- [ ] **All 5 pickups' colliders confirmed `isTrigger == true` in the live scene** (mirrors Task 3.2's existing hazard check; catches the exact misconfiguration Decision 15's `Awake()` guards exist to flag)
- [ ] `Ground`, `Player`, `Main Camera`, `Bench`, `TestDamageHazard` Transforms/components byte-identical to pre-task state — **regression check**

---

**Phase 3 dependency graph:**
```
1.3 ──→ 3.1 [GAMEPLAY] PlayerHealth on Player ──→ 3.2 [GAMEPLAY] hazard placement ──→ 3.3 [GAMEPLAY] pickup placement ──→ (4.4, Phase 4)
2.3 ──────────────────────────────────────────────┘
2.1, 2.2 ──────────────────────────────────────────────────────────────────────────┘
(strict linear chain — see cross-phase sequencing note; no task here runs parallel with any other task in this phase)
```

---

## Phase 4 — `[ART]` HUD sprites, `[UI]` HUD implementation

#### Task 4.1: `[ART]` Derive the filled Mask HUD icon sprite
**Depends on:** none
**Parallel:** yes — with all of Phases 1–3; Task 4.2 is downstream of this
**Touches:** `Assets/Sprites/Reference/health.png` (read-only source, not modified); creates `Assets/Sprites/UI/Mask/Mask_Filled.png` (+`.meta`, new)
**Regression risk:** none (new file) — but per `CLAUDE.md`'s "Prioritize Unity's own tools," the crop/knockout must run through the live Unity Editor's existing, unmodified `QuirrelReferenceSpriteImporter`, not a Python reimplementation.

Implementation: run the existing, unmodified `QuirrelReferenceSpriteImporter.CropResizeAndKnockout` against `Assets/Sprites/Reference/health.png` via the live Unity Editor, at a target content height chosen by visual comparison against the roster's existing sprite sizes. Output to scratchpad first (off-tree-iteration discipline). Accept/reject at ≥400% zoom against a contrasting checkerboard, no halo/fringe; color spot-checked against `ART.md` §2.2's "Mask, healthy" (`#F2EEE3`) / "Mask ink line" (`#1B1B1F`). Import Sprite Mode Single, Pivot Center (0.5, 0.5) — UI icons use the sheet-level baseline per §7, not the per-frame-character Bottom pivot — Filter Bilinear, sRGB on, Alpha Is Transparency on.

**Acceptance criteria:**
- [ ] Crop/knockout regeneration runs through the live Unity Editor's existing tool, not a Python reimplementation
- [ ] `Mask_Filled.png` shows no white halo/fringe at ≥400% zoom against a checkerboard
- [ ] Fill/line colors spot-checked against `ART.md` §2.2's hex values — no deviation, or corrected before promotion
- [ ] `health.png` byte-identical before and after — **regression check**
- [ ] Imported with Sprite Mode Single, Pivot Center (0.5, 0.5), Filter Bilinear, sRGB on, Alpha Is Transparency on — confirmed live in the Unity Inspector

---

#### Task 4.2: `[ART]` Derive the empty Mask HUD icon sprite; `ART.md` updates
**Depends on:** Task 4.1 (derives from the promoted `Mask_Filled.png`)
**Parallel:** yes — with all of Phases 1–3, and with Task 4.3
**Touches:** creates `Assets/Sprites/UI/Mask/Mask_Empty.png` (+`.meta`, new); `ART.md` §2.4, §9
**Regression risk:** none (new file/additive doc edits).

Implementation: derive `Mask_Empty.png` from the promoted `Mask_Filled.png` using ImageMagick (`magick`, on PATH) — a `-fuzz`/`-transparent` color-key operation replacing the interior `#F2EEE3` cream fill with full transparency while leaving `#1B1B1F` ink-line pixels opaque. This is a generic image-editing op, not a reimplementation of Unity's own import/knockout logic. `ART.md` §2.4 gains two new rows: "Mask pip, filled (HUD)" (references existing §2.2 tokens) and "Mask pip, empty (HUD)" (`#1B1B1F` outline on transparent fill), each noting this is the non-diegetic upper-left HUD, distinct from §2.5's separate diegetic system. `ART.md` §9 gains one new bullet recording the Section 0.1 deferral.

**Acceptance criteria:**
- [ ] `Mask_Empty.png`'s interior fill fully transparent (alpha 0), ink-line pixels remain opaque — confirmed by a pixel-level check (`py -3.13` + Pillow, read-only diagnosis)
- [ ] No halo/fringe at ≥400% zoom against a checkerboard
- [ ] `ART.md` §2.4 gains exactly the two new rows described above; §9 gains exactly one new deferral bullet; no other section edited
- [ ] Imported with the same settings as Task 4.1's sprite, confirmed live in the Inspector

---

#### Task 4.3: `[UI]` `Quirrel.UI` assembly + `MaskHUD.cs` render logic
**Depends on:** Task 4.1, Task 4.2 (sprite assets referenced by tests, though the script compiles without them)
**Parallel:** yes — with Phases 1–3 in full
**Touches:** none existing — creates `Assets/Scripts/UI/Quirrel.UI.asmdef` (new), `Assets/Scripts/UI/MaskHUD.cs` (new)
**Regression risk:** none (new assembly/file). **`Quirrel.UI.asmdef`'s `references` array must explicitly retain both `"Quirrel.Player"` and `"UnityEngine.UI"`** — `UnityEngine.UI.Image` lives in the separate `com.unity.ugui` package assembly, not Unity's auto-referenced core assemblies (confirmed: every existing consumer asmdef in the package cache references it by the plain string `"UnityEngine.UI"`, matching this project's own by-name asmdef reference convention); `Quirrel.Player` is needed so `MaskHUD` can read `PlayerHealth`. Omitting either fails to compile or fails to resolve.

Implementation: `[SerializeField] private PlayerHealth _playerHealth;`, `[SerializeField] private Image[] _pipSlots;` (expected length `PlayerHealth.MaxMasksCap` = 9), `[SerializeField] private Sprite _filledSprite, _emptySprite;`. `OnEnable`/`Start`: assert `_pipSlots.Length == PlayerHealth.MaxMasksCap`, `Debug.LogError` (not silent) if mismatched (Decision 7). `OnEnable`/`OnDisable` subscribe/unsubscribe `_playerHealth.OnHealthChanged += Refresh`. `Start()` also calls `Refresh()` once directly, in case `OnHealthChanged` hasn't fired yet. `public void Refresh()`: `filledPipCount = _playerHealth.CurrentQuarterMasks / PlayerHealth.QuartersPerMask`; for `i` in `0..8`: `_pipSlots[i].gameObject.SetActive(i < _playerHealth.MaxMasks)`, sprite = filled if `i < filledPipCount` else empty.

**Acceptance criteria:**
- [ ] `Quirrel.UI.asmdef`'s `references` explicitly lists both `"Quirrel.Player"` and `"UnityEngine.UI"` — confirmed the project compiles with `Image` used in `MaskHUD.cs`
- [ ] `Refresh()` with `MaxMasks == 4`, `CurrentQuarterMasks == 16`: pips 0–3 active/filled, pips 4–8 inactive
- [ ] `Refresh()` with `MaxMasks == 4`, `CurrentQuarterMasks == 9`: pips 0–1 filled, pips 2–3 empty, pips 4–8 inactive — the rounds-down proof
- [ ] `Refresh()` with `MaxMasks == 9`: pips 0–8 all active
- [ ] `Refresh()` with a null `_playerHealth` reference does not throw (defensive, logged)
- [ ] A `_pipSlots` array of length ≠ 9 logs a `Debug.LogError` on `OnEnable`/`Start` rather than silently under/over-indexing
- [ ] Subscribing/unsubscribing via `OnEnable`/`OnDisable` confirmed by a test that disables the component, changes health, and confirms `Refresh` was not called

---

#### Task 4.4: `[UI]` Scene wiring — Canvas, 9-pip hierarchy, `MaskHUD` assignment
**Depends on:** Task 4.1, Task 4.2, Task 4.3, Task 3.1, Task 3.3 (same-scene-file sequencing — final link in the Phase 3 chain)
**Parallel:** no — last link in the cross-phase scene chain
**Touches:** `Assets/Scenes/SampleScene.unity` (adds a `Canvas` + `EventSystem` + 9 child `Image` GameObjects under a `Horizontal Layout Group`; assigns `MaskHUD`'s serialized fields)
**Regression risk:** shared scene file, same discipline as Tasks 3.1–3.3.

Implementation: `Canvas` (Screen Space – Overlay), `CanvasScaler` "Scale With Screen Size," reference resolution 1920×1080 (`ART.md` §7, first real use in this project). Container `RectTransform` anchored top-left (anchor min/max (0,1), pivot (0,1)) with a `Horizontal Layout Group`, 9 child `Image` GameObjects (initial sprite `Mask_Filled`, all active by default). `MaskHUD` added, all fields assigned.

**Acceptance criteria:**
- [ ] `CanvasScaler`: "Scale With Screen Size," reference resolution 1920×1080, matching `ART.md` §7 exactly
- [ ] The 9-pip row renders in the upper-left corner in Play Mode
- [ ] `MaskHUD`'s serialized fields all non-null: 9 `Image` slots, both sprites, the scene's `PlayerHealth`
- [ ] Live Play Mode check: taking damage immediately updates the HUD's filled-pip count with no visible one-frame lag
- [ ] Live Play Mode check: resting on the Bench immediately restores the HUD to fully filled
- [ ] Live Play Mode check: collecting all 4 test Mask Shards adds a 5th visible (initially empty) pip
- [ ] Every pre-existing GameObject in `SampleScene.unity` confirmed byte-identical to its pre-task state — **regression check**

---

**Phase 4 dependency graph:**
```
4.1 [ART] filled sprite ──→ 4.2 [ART] empty sprite + ART.md ──┐
                                                                 ├──→ 4.3 [UI] Quirrel.UI + MaskHUD.cs ──→ 4.4 [UI] scene wiring
                                                                 ┘         (also depends on 3.1, 3.3 — final link, Phase 3 chain)
(4.1→4.2 sequential, same-derivation-chain; 4.3 depends on both; 4.4 depends on 4.1/4.2/4.3 plus the Phase 3 scene chain)
```

---

## Phase 5 — `[QA]` Tests and manual playtest addendum

#### Task 5.1: `[QA]` `PlayerHealthTests.cs` — EditMode coverage
**Depends on:** Task 1.2, Task 1.3
**Parallel:** yes — with Task 5.2, Task 5.5
**Touches:** `Assets/Scripts/Player/Tests/EditMode/PlayerHealthTests.cs` (new)
**Regression risk:** additive only.

**Acceptance criteria:**
- [ ] All acceptance criteria from Tasks 1.2/1.3 each backed by at least one passing test
- [ ] No test calls `Input.GetKey`/`GetKeyDown` directly, consistent with this file's existing convention
- [ ] All tests pass

---

#### Task 5.2: `[QA]` `MaskShardPickupTests.cs`, `MaskPickupTests.cs`, `TestDamageHazardTests.cs`
**Depends on:** Task 2.1, Task 2.2, Task 2.3
**Parallel:** yes — with Task 5.1, Task 5.5
**Touches:** `Assets/Scripts/Environment/Tests/EditMode/MaskShardPickupTests.cs`, `MaskPickupTests.cs`, `TestDamageHazardTests.cs` (new) — land in the existing `Assets/Scripts/Environment/Tests/EditMode/Quirrel.Environment.EditModeTests.asmdef` (confirmed present on disk, alongside `BenchTests.cs`; no new test assembly needed)
**Regression risk:** additive only. Full pre-existing `BenchTests.cs` suite must pass unmodified — **regression check**.

**Acceptance criteria:**
- [ ] All acceptance criteria from Tasks 2.1/2.2/2.3 each backed by at least one passing test, using `BenchTests.cs`'s existing fixture style, extended to also `AddComponent<PlayerHealth>()`
- [ ] New test: a non-trigger `Collider2D` on `MaskShardPickup`/`MaskPickup` logs a `Debug.LogError` via `Awake()`
- [ ] Pre-existing `BenchTests.cs` suite passes unmodified — **regression check**

---

#### Task 5.3: `[QA]` `PlayerControllerTests.cs` — `Rested` event coverage + full regression re-run
**Depends on:** Task 1.1
**Parallel:** yes — with Task 5.1, 5.2, 5.5
**Touches:** `Assets/Scripts/Player/Tests/EditMode/PlayerControllerTests.cs`
**Regression risk:** additive only.

**Acceptance criteria:**
- [ ] New test: `TrySit_OnSuccess_InvokesRestedEvent`
- [ ] New test: `TrySit_OnFailure_DoesNotInvokeRestedEvent`
- [ ] New test confirming `CheckInitialSpawnSit`'s auto-sit path also invokes `Rested`
- [ ] Full pre-existing `PlayerControllerTests` suite passes unmodified — **regression check**
- [ ] `AnimatorContractTests.cs` full suite re-run and confirmed still green — **regression check**
- [ ] `Assets/Scripts/Environment/Tests/EditMode/BenchTests.cs` full suite re-run and confirmed still green — **regression check**
- [ ] `Assets/Scripts/Camera/Tests/EditMode/CameraFollowTests.cs` full suite re-run and confirmed still green — **regression check**

---

#### Task 5.4: `[QA]` `PlayerHealthPlayModeTests.cs` — real-physics hazard/invulnerability/rest coverage
**Depends on:** Task 3.2, Task 3.3, Task 4.4
**Parallel:** yes — with Task 5.1, 5.2, 5.3, 5.5
**Touches:** `Assets/Scripts/Player/Tests/PlayMode/PlayerHealthPlayModeTests.cs` (new)
**Regression risk:** additive only.

Implementation: minimal rig (`TestGround` + `TestPlayer` with `Rigidbody2D`/`BoxCollider2D`/`SpriteRenderer`/`PlayerController`/`PlayerHealth`, plus a `TestHazard` GameObject with `TestDamageHazard`), built in `[UnitySetUp]`, mirroring `PlayerControllerPlayModeTests.cs`'s confirmed own rig-over-scene-load precedent.

**Acceptance criteria:**
- [ ] Moving the player through the hazard's trigger volume under real, stepped physics reduces `CurrentQuarterMasks` by exactly 1 on entry
- [ ] Holding the player stationary inside the hazard's trigger volume across many physics frames (`yield return null` loop spanning several seconds) does not drain health beyond the single entry hit
- [ ] **The actual proof of the invulnerability window: move the player out of the hazard's trigger and immediately back in within the 1.0s invulnerability window → the second entry deals zero additional damage. Then move out, wait past 1.0s (e.g. `yield return new WaitForSeconds(1.1f)`), and re-enter → a further chip of damage is taken.** This is the one scenario that actually distinguishes "invulnerability window present and working" from "Enter-only firing alone happens to prevent drain," which a stationary-only criterion cannot distinguish.
- [ ] Successfully sitting (via the rig's own `PlayerController.TrySit`) while damaged triggers a full heal via the live `Rested` event path under real Play Mode timing
- [ ] All existing `PlayerControllerPlayModeTests.cs` tests pass unmodified — **regression check**

---

#### Task 5.5: `[QA]` `Quirrel.UI.EditModeTests` — `MaskHUDTests.cs`
**Depends on:** Task 4.3
**Parallel:** yes — with Task 5.1, 5.2, 5.3, 5.4
**Touches:** `Assets/Scripts/UI/Tests/EditMode/Quirrel.UI.EditModeTests.asmdef` (new, references `Quirrel.UI`, `Quirrel.Player`, `UnityEngine.UI`, `UnityEngine.TestRunner`, `UnityEditor.TestRunner`), `Assets/Scripts/UI/Tests/EditMode/MaskHUDTests.cs` (new)
**Regression risk:** none (new assembly/file).

**Acceptance criteria:**
- [ ] All acceptance criteria from Task 4.3 each backed by at least one passing test, including the new `_pipSlots.Length` mismatch → `Debug.LogError` case
- [ ] Tests construct a bare `GameObject` hierarchy directly, without loading any scene

---

#### Task 5.6: `[QA]` Manual playtest protocol addendum — new `5f` section
**Depends on:** Task 3.1, 3.2, 3.3, 4.4, 5.1, 5.2, 5.3, 5.4, 5.5
**Parallel:** no
**Touches:** `Docs/Plans/002_manual-playtest-protocol.md` (new `5f` section, inserted before `## 6. Sign-off`)
**Regression risk:** none to code — documentation only.

New `5f` rows: **5f.1** HUD shows 4 filled pips at game start, upper-left. **5f.2** Touching the hazard wall once chips exactly 1 quarter-mask; HUD's visible pip count only changes once a full mask's worth (4 quarters) is lost, confirmed as expected (Decision 8), not a bug. **5f.3** Bumping in/out of the hazard's trigger rapidly does not deal a second hit within ~1 second; waiting past that window and re-entering does deal another hit. **5f.4** Collecting all 4 test Mask Shards adds a 5th, initially-empty pip — not automatically filled (Decision 3). **5f.5** Collecting the test Mask pickup restores the HUD to fully filled regardless of current damage. **5f.6** Sitting on the Bench while damaged restores the HUD to fully filled. **5f.7** Taking enough hits to reach 0 health plays the Die animation and permanently locks input — confirmed as a known, accepted dead-end, not a regression this plan introduced. **5f.8** Regression re-check of `5a`–`5e`.

**Acceptance criteria:**
- [ ] New `5f` section added with all rows above, following the `5a`–`5e` format exactly
- [ ] Live verification performed and recorded PASS/FAIL for every new `5f` row
- [ ] Any FAIL logged as a new bug per this doc's existing bug-report format, routed back through the pipeline, not silently patched inside this task
- [ ] `5a`–`5e` sections re-confirmed to still read correctly — **regression check**
- [ ] Explicit negative-confirmation: this plan's `ART.md` change is confirmed to be exactly the two rows + one bullet described in Task 4.2, nothing more

---

**Phase 5 dependency graph:**
```
1.2, 1.3 ──→ 5.1 [QA] PlayerHealth tests ──────────────┐
2.1,2.2,2.3 → 5.2 [QA] pickup/hazard tests ─────────────┤
1.1 ────────→ 5.3 [QA] Rested + regression ─────────────┼──→ 5.6 [QA] manual playtest addendum
3.2,3.3,4.4 → 5.4 [QA] PlayMode hazard/invuln/rest tests ┤
4.3 ────────→ 5.5 [QA] MaskHUD tests ────────────────────┘
(5.1–5.5 mutually parallel — different files/assemblies; 5.6 needs the whole feature + all green suites)
```

---

## Explicitly out of scope for this plan

Enemy AI of any kind; Soul/Focus resource, self-heal ability, or Soul-meter UI (§2.4's reserved `#BFE3FF` token stays reserved, unused); Hot Spring prop; save/load, schema, or persistence of any Mask/shard state; checkpoint/respawn on death (dying remains a permanent dead-end state, a pre-existing gap this plan does not fix); any `ProjectSettings`/tags/physics-layers/collision-matrix change; any Animator Controller change; partial/"cracked" pip art beyond the 2 states Decision 8 covers; `ART.md` §2.5's diegetic on-character crack system (explicitly deferred, Section 0.1); a visual invulnerability flicker/flash effect (no VFX added for the i-frame window); real, hand-placed shippable level content beyond QA-verification placements; an Input Actions migration.

---

## Judgment calls made explicit

1. `PlayerHealth` lives inside the existing `Quirrel.Player` assembly, `MaskShardPickup`/`MaskPickup`/`TestDamageHazard` inside the existing `Quirrel.Environment` assembly — no asmdef graph change anywhere (Decisions 1, 14).
2. Mask Shards raise `MaxMasks` only, never healing current health directly — the user's own explicit, settled instruction (Decision 3).
3. The invulnerability window's justification is stated honestly as forward-looking i-frame infrastructure, not a strict requirement of today's literal QA hazard — Enter-only firing (Decision 11) already prevents the in-place-drain scenario independently (Decision 6).
4. `MaxMasksCap` is a shared compile-time constant, not a serialized tunable, closing a real silent-failure risk (pips beyond a raised cap rendering nothing, with no error) rather than only documenting it (Decision 7).
5. The HUD renders only 2 pip states with a named, accepted rounding limitation for the sub-Mask QA hazard's chip damage (Decision 8).
6. `PlayerController` gains exactly one new line of surface area (Decision 9).
7. The test hazard is entirely invisible in Play Mode by design, with its temporary/non-shippable nature stated in its own class doc comment (Decision 12).
8. **Section 0.1's design tension (this plan's HUD vs. `ART.md` §4.1's diegetic-health-surface philosophy) was surfaced explicitly and independently reviewed — confirmed reasonable, not blocking.** Implemented per the user's literal, concrete feature spec and the genre's own convention, with `ART.md` §9 updated to record the deferral rather than any rewrite of §4.1 itself.
9. The pickups' defensive `isTrigger` check generalizes to `GetComponent<Collider2D>()` rather than literally copying `Bench.Awake()`'s `BoxCollider2D`-specific check, since this plan doesn't fix the pickups' final collider shape (Decision 15).

---

## Reference file paths consulted while drafting this plan

- `C:\Dev\HollowKnightQuirrel\HollowKnightQuirrel\CLAUDE.md`, `ART.md`
- `Docs\Plans\008_bench-sit-mechanic.md`, `009_bench-visual-fixes.md`, `002_manual-playtest-protocol.md`
- `Assets\Scripts\Player\PlayerController.cs`, `Assets\Scripts\Environment\Bench.cs`, `Assets\Scripts\Player\IBenchSeat.cs`
- `Assets\Scripts\Player\Tests\EditMode\PlayerControllerTests.cs`, `AnimatorContractTests.cs`
- `Assets\Scripts\Player\Tests\PlayMode\PlayerControllerPlayModeTests.cs`
- `Assets\Scripts\Environment\Tests\EditMode\BenchTests.cs`, `Quirrel.Environment.EditModeTests.asmdef`
- `Assets\Animations\Quirrel.controller`, `Assets\Scenes\SampleScene.unity`, `Assets\Sprites\Reference\health.png`
- `Assets\Scripts\Player\Quirrel.Player.asmdef`, `Assets\Scripts\Environment\Quirrel.Environment.asmdef`
- `Library\PackageCache\com.unity.ugui@1.0.0\Runtime\UnityEngine.UI.asmdef`

---

**Approved for implementation.** Per this repo's pipeline, this plan file is committed and pushed before any implementation agent (`gameplay-programmer`, `ui-developer`, `art-director`, `qa-engineer`) begins work on it.
