# Implementation Plan — Pin Head Recolor: Gold/Amber → "Verdant" Green

**Status:** 🟡 DRAFT
**Author:** implementation-plan-architect
**Date:** 2026-08-02
**Feature:** Recolor Quirrel's pin weapon head from gold/amber to the canonical Verdant green
(`#6FA25C` / `#4A7A3E`) across `Assets/Sprites/Quirrel_Sprites.png`
**Closes:** ART.md Section 9, bullet 1 ("Pin head color")
**Estimated total:** ~20 hours across 10 tasks

---

## 0. Executive Summary

This is a color-only correction to a single 1408×768 PNG, affecting **~8,459 pixels (0.78% of
the image)** across **19 of the sheet's 24 frames**. On paper it is trivial.

It is planned carefully anyway, for three reasons specific to this repo:

1. **It is a destructive, in-place edit to the only character asset the game has.** There is no
   second copy of a recolored sheet to fall back to, no prefab to revert, and the file is stored
   in Git LFS — so a bad edit is not visible in a text diff during review.
2. **The sheet does not match what ART.md says it contains** (Section 1.3 below). Acceptance
   criteria written against ART.md's stated frame map would be unverifiable, because the frame
   indices that ART.md cites do not exist. That has to be corrected first or this task cannot be
   signed off.
3. **A PNG re-save can silently shift every pixel in the sheet**, not just the 0.78% being
   targeted, via PNG gamma/color-profile chunks that Unity's importer honours (Section 4.1).
   This is the single highest-severity risk in the task and it is invisible to a visual check of
   the pin head alone.

**Answer to the routing question in the request — does grid-slicing block this?**
**No, and the recolor should go first.** See Section 3 for the reasoning and for two blockers
discovered in the sheet that make the "grid-slicing pipeline task" in ART.md Section 9 a
separate, larger plan than currently described.

---

## 1. Verified Project State

Everything in this section was read from the repo, not assumed. Where it contradicts ART.md,
the repo wins and ART.md is corrected by Task 0.1.

### 1.1 Engine and pipeline

| Fact | Value | Source |
|---|---|---|
| Unity version | 2021.3.45f2 | `ProjectSettings/ProjectVersion.txt` |
| Render pipeline | Built-in (no URP package present) | `Packages/manifest.json` |
| 2D feature set | `com.unity.feature.2d` 2.0.1 | `Packages/manifest.json` |
| Shader Graph / Light2D | **not available** (URP-gated) | absence in `manifest.json` |

Consequence: any *runtime* recolor approach would require a hand-written Built-in-RP shader plus
a per-pixel region mask texture. See Section 2 for why that is rejected for this task.

### 1.2 Current repo contents (the real blast radius)

`Assets/` contains, in total:

```
Assets/Scenes/SampleScene.unity
Assets/Sprites/Quirrel_Sprites.png            <- the target
Assets/Sprites/Reference/Quirrel sprites.png  <- untouched original generation
Assets/Sprites/Reference/Quirrel.png
```

There are **no** scripts, no `.asmdef` files, no prefabs, no AnimationClips, no
AnimatorControllers, no ScriptableObjects, no save system, and no sprite atlases. `SampleScene`
is the untouched Unity default.

**This is the key scheduling insight: the downstream reference count of this sprite sheet is
currently zero.** Every week that passes, that number only grows — clips, an Animator, a player
prefab, an atlas, and eventually a pin-tier system will all attach to this asset. The blast
radius of this recolor will never again be as small as it is today. That argues for doing it
now, ahead of slicing and ahead of any Animator work.

### 1.3 The sprite sheet as it actually is

`Assets/Sprites/Quirrel_Sprites.png` — measured directly:

| Property | Measured value | Implication |
|---|---|---|
| Dimensions | 1408 × 768 | Under the 2048 max size; no resize needed |
| Color type | 6 (RGBA), 8-bit, non-interlaced | |
| Alpha channel | **255 for 100% of pixels** | Background is opaque white, not transparent |
| Distinct opaque colors | **29,485** | Anti-aliased continuous tone, *not* indexed pixel art |
| Ancillary PNG chunks | `sRGB` (intent 0), `gAMA` (0.45455), `pHYs`, `iTXt` (XMP), `caBX` | See risk R1, Section 4.1 |
| Git storage | LFS (`*.png` is LFS-tracked via `.gitattributes`) | Binary; no reviewable text diff |
| Importer `guid` | `62b4e791ec637c245b6ea820090a2f94` | Must survive the edit — see risk R2 |

Importer settings (from `Quirrel_Sprites.png.meta`), all matching ART.md Section 7:
`spriteMode: 1` (Single), `sprites: []` (no slices authored), PPU 100, pivot (0.5, 0.5),
`filterMode: 1` (Bilinear), maxTextureSize 2048, `sRGBTexture: 1`, `alphaIsTransparency: 1`,
`ignorePngGamma: 0`.

### 1.4 ⚠ Frame map: ART.md is wrong, and the recolor depends on the correct one

ART.md Section 7 states the sheet contains 34 frames (Idle 4, Walk 8, Jump 4, Attack 6,
Defend 4, Hurt 2, Die 6). Sections 2.5 and 4.1 cite "**DIE frames 29–34**" and
"**ATTACK frames 17–22**".

The sheet actually contains **24 frames**. Frame indices 25–34 do not exist. Measured layout,
from connected-component analysis of all non-white pixels:

| State | ART.md claims | **Actual** | Canonical frame IDs (this plan) |
|---|---|---|---|
| IDLE | 4 | **4** ✅ | F1–F4 |
| WALK | 8 | **6** ❌ | F5–F10 |
| JUMP | 4 | **3** ❌ | F11–F13 |
| HURT | 2 | **1** ❌ | F14 |
| ATTACK (swing) | 6 | **4** ❌ | F15–F18 |
| DEFEND (block) | 4 | **3** ❌ | F19–F21 |
| DIE | 6 | **3** ❌ | F22–F24 |
| **Total** | 34 | **24** | |

Two further ART.md claims are also contradicted by the pixels:

- **Section 9, bullet 2** states frame 29 (DIE start) "shows an orange impact flash." There are
  **zero** warm-hue pixels anywhere in the DIE band (x 780–1380, y 560–760). The HURT frame's
  impact motes are neutral grey. The reported conflict with the infection-orange reservation
  (Section 2.6) **does not exist in this asset.**
- **Sections 2.5 / 4.1** describe the DIE sequence as "mask separates/shatters." The three
  actual DIE frames are a body collapse; the mask stays attached. Mask-shatter art does not
  exist yet.

Both are **out of scope for this plan** and are routed back through the pipeline as separate
tasks (Section 6). Task 0.1 corrects only what this recolor's acceptance criteria depend on.

### 1.5 Where the pin head actually is

Per-frame pin-head regions, measured (bounding boxes in sheet pixel coordinates, origin
top-left; "area" = warm-hue pixel count at the inclusive selection threshold in Section 2.2):

| Frame | State | Frame bbox (x, y) | Pin-head region(s) | Area (px) |
|---|---|---|---|---|
| F1 | IDLE 1 | 61–161, 41–172 | 127–145, 103–130 | 239 |
| F2 | IDLE 2 | 196–296, 41–172 | 261–279, 103–130 | 247 |
| F3 | IDLE 3 | 329–432, 41–172 | 398–415, 102–130 | 239 |
| F4 | IDLE 4 | 463–567, 41–172 | 532–548, 102–130 | 237 |
| F5 | WALK 1 | 58–164, 218–349 | 127–163, 274–309 | 478 |
| F6 | WALK 2 | 196–307, 218–348 | 271–307, 269–304 | 478 |
| F7 | WALK 3 | 334–448, 216–349 | 410–446, 267–303 | 407 |
| F8 | WALK 4 | 483–599, 216–348 | 561–596, 272–307 | 407 |
| F9 | WALK 5 | 626–738, 217–349 | 701–736, 273–308 | 496 |
| F10 | WALK 6 | 771–881, 216–347 | 844–880, 269–302 | 424 |
| F11 | JUMP 1 (crouch) | 937–1052, 208–330 | 1015–1049, 266–299 | 423 |
| F12 | JUMP 2 (apex) | 1084–1215, 151–293 | 1178–1214, 200–235 | 414 |
| F13 | JUMP 3 (descend) | 1241–1364, 197–337 | 1325–1363, 266–298 | 437 |
| **F14** | **HURT** | 54–178, 407–550 | **none — pin not visible** | 0 |
| F15 | ATTACK 1 (wind-up) | 466–614, 412–552 | 489–506, 420–451 | 160 |
| F16 | ATTACK 2 (swing arc) | 680–924, 411–551 | 820–831, 483–513 | 219 |
| F17 | ATTACK 3 | 935–1100, 415–552 | 983–1003, 472–499 | 387 |
| F18 | ATTACK 4 (extended) | 1100–1308, 415–549 | 1212–1235, 473–504 | 308 |
| **F19** | **DEFEND 1 (pre-raise)** | 50–163, 607–750 | **none — pin not visible** | 0 |
| F20 | DEFEND 2 | 224–424, 610–751 | 400–422, 655–690 **+** 321–337, 672–675 | 633 + 44 |
| F21 | DEFEND 3 (held forward) | 457–629, 611–750 | 551–574, 649–695 **+** 604–627, 652–687 | 523 + 644 |
| **F22–F24** | **DIE 1–3** | 806–927 / 983–1142 / 1187–1368 | **none — pin not visible** | 0 |

Findings that shape the plan:

- **19 of 24 frames carry pin-head pixels.** Five do not: F14 (HURT), F19 (DEFEND 1), and
  F22–F24 (DIE). These five are a *verification asset*: they must be provably byte-identical
  after the edit, which is a cheap, strong check that the selection mask did not over-reach.
- **DEFEND frames carry two warm regions each** — the head disc *and* a colored collar near the
  grip. Both must be recolored; treating only the large disc as "the head" would leave an amber
  collar next to a green disc on the game's most color-legible pose (ART.md 4.2 makes the DEFEND
  head the parry read). F20 and F21 are the largest and most prominent pin-head areas in the
  sheet at 677 px and 1,167 px respectively.
- **The 44 px region in F20 (x321–337) and the 17 px fragment at (534, 672) are easy to miss** —
  they are small, partially occluded, and would read as amber specks on a green pin.
- **Two warm-ish pixels sit inside the baked-in "IDLE:" text label** at (97, 21) and (95, 22)
  — inside the label bbox x43–99, y14–36. A naïve whole-image color-range selection will
  recolor part of the text. See risk R3.
- 85 fringe fragments under 20 px (135 px total) exist at head/shaft/ink-line boundaries. These
  are anti-aliasing, and they are the fringe-halo risk in Task 1.3.

### 1.6 Source colors: two clusters, not one amber

Hue histogram of the selected pin pixels (5° bins, hue in degrees):

| Hue band | Pixels | Reads as |
|---|---|---|
| 5–25° | ~1,824 | Red-orange — head shadow side and rim |
| 30–55° | ~4,751 | Gold/amber — head lit side and highlight |
| 55–70° | ~452 | Yellow highlight core |

Most-frequent single value: `#E2C83D` (hue 50.5°, S 0.73, V 0.89).

**ART.md and the request both describe the current head as "gold/amber." It is actually a
two-cluster shaded amber with a substantial red-orange shadow side.** Any approach that selects
"the gold" and replaces it will leave a red-orange crescent on every one of the 19 heads. The
selection rule in Task 1.1 must span both clusters.

---

## 2. Technical Approach

### 2.1 Approach selection

| Option | Verdict |
|---|---|
| **A. Offline pixel edit of the PNG, in place** | **Selected.** Zero new runtime systems, zero new assets in `Assets/`, GUID and import settings preserved, matches ART.md's flip-book pipeline, and is trivially revertible via LFS checkout. |
| B. Runtime tint via material/shader | **Rejected for now.** Built-in RP with no Shader Graph means a hand-written shader; tinting only the head requires a companion mask texture shipped alongside the sheet; and it adds a runtime system to solve an authoring-time mistake. Violates YAGNI and additive-over-invasive. |
| C. Split the pin head onto a separate sprite layer | **Rejected for now.** Requires re-authoring the art (the head is drawn inline, overlapping the hand and shaft) — a shape change, which this task explicitly is not. |

**Forward note, not a task:** ART.md 2.3 defines four pin tiers (Verdant / Ember / Crimson /
Bone-white). When tier art is actually needed, option B or C becomes the right answer, and the
pin-head selection mask produced by Task 1.1 is exactly the input such a system needs. The mask
is a necessary intermediate of doing (A) regardless, so keeping it costs nothing. **This plan
builds no tinting system and no tier assets** — it only avoids throwing away an artifact it has
to create anyway.

### 2.2 The color mapping (this is the part that is easy to get wrong)

Target, from ART.md 2.3: highlight `#6FA25C`, shadow `#4A7A3E`.

| | Current gold highlight `#E2C83D` | Target `#6FA25C` | Target shadow `#4A7A3E` |
|---|---|---|---|
| Hue | 50.5° | 103.7° | 108.0° |
| Saturation | 0.730 | 0.432 | 0.492 |
| Value | 0.886 | 0.635 | 0.478 |

**A hue rotation alone is wrong.** Rotating +53° while preserving S 0.73 / V 0.89 yields a
fluorescent lime, which violates ART.md's "high contrast, low chroma" pillar (Section 1) and the
anti-slop guard against saturated decoration (Section 8). The mapping must also **desaturate by
roughly 40% and darken by roughly 28%** at the highlight end.

The implementing agent defines the mapping as a continuous transform across the source hue range
so that the head's existing 2–3-step cel shading survives as green shading, rather than
flattening to a single flat green — ART.md Section 3 requires 2–3 tone steps on characters and
explicitly forbids flat-fill and forbids smooth gradients. The two ART.md hexes are the
**anchor points** (lit face → `#6FA25C`, shadow face → `#4A7A3E`), not the only two output
values permitted; anti-aliased boundary pixels will legitimately land between them.

**Selection rule (starting point for Task 1.1, to be validated, not assumed):** HSV with
hue ∈ [0°, 80°], S > 0.15, V > 0.20, **intersected with a hand-verified per-frame region mask**
so that the text labels and any future warm pixels elsewhere cannot be caught. The threshold
alone selects 8,459 px; the strict variant (S > 0.30, V > 0.30, hue 5–70°) selects 6,575 px. The
1,884 px difference is the anti-aliased boundary, which is precisely the fringe that Task 1.3
exists to handle. Neither threshold is adopted blind — Task 1.1 verifies the resulting mask
against the 24-frame inventory in Section 1.5 before any pixel is written.

### 2.3 Working off-tree, then a single promotion

All iteration happens on a **working copy outside `Assets/`** (proposed: `Docs/Sprites/`, which
already exists and is empty). Only Task 1.4 writes to the tracked asset, once, after the working
copy is approved.

Rationale:
- Files placed under `Assets/` are imported by Unity, get a `.meta` and a new GUID, and are
  candidates for atlasing and for inclusion in a build. Iteration artifacts must not become
  game assets by accident.
- One write to the tracked PNG means **one** new Git LFS object rather than one per iteration
  (each is ~0.9 MB, permanent in LFS history).
- A fringed or half-remapped intermediate is never committed to `Assets/`, so `main` is never in
  a state where the character's weapon is visibly broken.

---

## 3. Interaction With the Pending Grid-Slicing Task

**Direct answer: grid-slicing is not a prerequisite for this recolor, and this recolor should be
sequenced first.**

- **Technically independent.** Slicing writes sprite rects into the `spriteSheet` block of the
  `.meta`; the recolor writes pixels in the `.png`. They touch different files and do not
  conflict.
- **Recolor-first is strictly safer.** Today, zero AnimationClips, zero Animator states and zero
  prefabs reference this sheet's sub-sprites. Once slicing lands and clips are authored, a later
  pixel edit is still safe *only* while dimensions are unchanged — but any tool that resizes,
  re-exports, or re-adds the file would invalidate every authored sprite rect and every clip
  keyframe referencing them. Doing the destructive edit while the reference count is zero
  removes that class of risk entirely.
- **Hand-off requirement (belongs to the slicing plan, not this one):** whoever does slicing or
  background knockout later must re-run this plan's Task 2.1 diff afterwards, to confirm the pin
  heads were not disturbed. Recorded in Task 3.1.

**Two blockers found in the sheet that the ART.md Section 9 slicing bullet does not mention.**
Both are out of scope here; both need their own plan before slicing is attempted:

1. **Uniform grid slicing is impossible on this sheet.** Frame widths range from 101 px (F1) to
   245 px (F16, the swing-arc frame). Row pitch varies. The JUMP frames are arranged as a rising
   diagonal staircase, not a row. And **F17 and F18 bounding boxes overlap in x** (F17 ends at
   x1100, F18 begins at x1100). Grid-By-Cell-Size will cut through frames. Slicing must be
   Automatic or manual — and Automatic will additionally emit the **nine baked-in text label
   components** ("IDLE:", "WALK:", "JUMP", "HURT", "ATTACK", "(SWING)", "DEFEND", "(BLOCK)",
   "DIE") as sprites.
2. **The sheet has no transparency at all** — alpha is 255 for every one of its 1,081,344
   pixels. Sliced sprites will render as opaque white rectangles in-engine. `alphaIsTransparency`
   is enabled in the importer, but there is no alpha for it to act on. A background-knockout pass
   is required before these frames are usable, independent of slicing.

---

## 4. Regression Risk Register

### 4.1 R1 — PNG gamma / color-profile chunk loss (severity: **high**, likelihood: **high**)

The file carries `sRGB` (intent 0), `gAMA` (0.45455), `pHYs`, `iTXt` (XMP) and a 20,308-byte
`caBX` chunk. The importer has `ignorePngGamma: 0`, meaning **Unity honours the PNG gamma
chunk**.

Most image editors and most export paths **drop, add, or rewrite these ancillary chunks on
save.** If `gAMA` or `sRGB` changes, *every pixel in the sheet* shifts in brightness on import —
Quirrel's mask, cloak, body and the ink line included — from an edit that was supposed to touch
0.78% of the image. It will not be caught by looking at the pin head, and it will not be caught
by a Git LFS diff.

**Mitigation:** Task 2.3 byte-compares the ancillary chunk set before and after, and Task 1.2's
acceptance criteria require the writer to preserve `sRGB`/`gAMA` verbatim. If a tool cannot
preserve them, the chunks are re-injected before promotion.

### 4.2 R2 — Asset GUID loss (severity: high, likelihood: low)

The importer GUID `62b4e791ec637c245b6ea820090a2f94` is the identity every future clip, prefab
and atlas will reference. **Deleting and re-adding the PNG generates a new GUID** and would
break every future reference. Task 1.4 therefore overwrites the file's bytes in place and never
deletes it; Task 2.3 verifies the `.meta` is unchanged.

### 4.3 R3 — Over-selection outside the pin (severity: medium, likelihood: medium)

Confirmed live: two warm pixels inside the baked-in "IDLE:" text label at (97, 21) and (95, 22)
fall inside a naïve whole-image color-range selection. Cream mask tones (`#F2EEE3`) sit close
enough in hue that a loosened threshold could begin catching mask anti-aliasing. Mitigated by
intersecting the color rule with a per-frame region mask (Task 1.1) and verified by the
untouched-pixel diff (Task 2.1).

### 4.4 R4 — Under-selection / amber remnants (severity: medium, likelihood: medium)

The red-orange cluster (hue 5–25°, ~1,824 px), the F20 collar fragment (44 px), and the
17 px fragment at (534, 672) are all easy to miss. Mitigated by the per-frame area reconciliation
in Task 2.1 against the table in Section 1.5.

### 4.5 R5 — Contrast and legibility regression (severity: medium, likelihood: medium)

The head goes from V 0.886 (gold highlight) to V 0.635 / 0.478 (Verdant). Three ART.md rules
must be re-checked, because the recolor changes the value relationship, not just the hue:

- **ART.md 4.2** requires point and head to stay distinct in **value *and* color** so attack-ready
  and block-ready poses read apart. Shaft is `#C9CCD1` (V 0.82) / `#9AA0AA` (V 0.667). Verdant at
  V 0.635/0.478 is *further* from the shaft than gold was (0.886 vs 0.82 was uncomfortably
  close), so this should improve — but it must be measured, not assumed.
- **ART.md 3** silhouette readability, and 2–3 tone steps preserved on the head.
- **ART.md 2.1** environment tones. A green head at V 0.478 risks losing contrast against
  midground washes and any green/fungal biome. This is a genuinely new risk that gold did not
  have, and it is checked in Task 2.2.

### 4.6 R6 — Reference copy divergence (severity: low, likelihood: medium)

`Assets/Sprites/Reference/Quirrel sprites.png` is the untouched original generation and is
**deliberately not recolored** — it is provenance. Without a note saying so, a later agent may
either "fix" it for consistency or regenerate the working sheet from it, silently reintroducing
gold. Task 3.1 records this.

### 4.7 Systems explicitly NOT at risk

Confirmed by inspection, so the QA pass need not scope them: no scripts, no asmdefs, no prefabs,
no AnimationClips, no AnimatorControllers, no ScriptableObjects, no save schema, no persisted
progression state, no ability gating, no room transitions, no atlases, no build scripts. No
`[DATA]` migration task is required. `SampleScene.unity` does not reference the sprite.

---

## Phase 0 — Baseline and Guardrails

#### Task 0.1: [ART] Correct the ART.md frame map to match the actual sheet
**Depends on:** none
**Parallel:** yes — with Task 0.2
**Touches:** `ART.md` (Sections 2.5, 4.1, 7)
**Regression risk:** ART.md is the project's single source of truth and is cited by five other
disciplines (Section 10). Correcting it changes numbers other agents may already have read.
Frame *indices* change meaning, so any in-flight work citing "frames 17–22" must be re-pointed.

Replace the Section 7 animation-timing frame counts with the measured values, and replace the
non-existent frame-index citations in Sections 2.5 and 4.1 with the canonical F1–F24 IDs from
Section 1.5 of this plan. Add the F1–F24 frame map as a short table so future work has one
authoritative index scheme. Record that the counts describe *the current asset*, and that ART.md's
previous counts should be read as the *target* for future re-authored art where they differ.

**Acceptance criteria:**
- [ ] ART.md Section 7 timing table states IDLE 4 / WALK 6 / JUMP 3 / HURT 1 / ATTACK 4 / DEFEND 3 / DIE 3, total 24
- [ ] Where a count is a future target rather than current reality, it is labelled as such and not silently overwritten
- [ ] Section 2.5 stage-4 citation no longer references "frames 29–34"; it references F22–F24 and notes that mask-shatter art does not yet exist
- [ ] Section 4.1 no longer references "frames 29–34"; Section 4.2 no longer references "frames 17–22" (uses F15–F18) and DEFEND uses F20–F21
- [ ] A canonical F1–F24 frame-ID table with per-frame bounding boxes is present in ART.md
- [ ] No other ART.md section is edited — palette, pillars and guards are untouched (regression check: diff shows changes confined to Sections 2.5, 4.1, 7)
- [ ] Every hex value in ART.md is unchanged (`#6FA25C` and `#4A7A3E` in particular)

---

#### Task 0.2: [QA] Capture the pre-change baseline
**Depends on:** none
**Parallel:** yes — with Task 0.1
**Touches:** none (new artifacts only, outside `Assets/`)
**Regression risk:** none — read-only against the sprite sheet

This is the characterization step in front of a destructive, LFS-stored, visually-reviewed edit.
Without it, "did anything else change?" is unanswerable after the fact.

Produce, under `Docs/Sprites/baseline/`: a per-frame crop set for all 24 frames using the
Section 1.5 bounding boxes; a full-image checksum; a per-frame checksum set; and a recorded
color inventory (distinct color count, warm-pixel count per frame, hue histogram). Record the
PNG ancillary chunk list and their byte contents so R1 can be checked later.

**Acceptance criteria:**
- [ ] 24 per-frame crops exist and are visually confirmed to contain one frame each
- [ ] Per-frame checksums recorded for all 24 frames, plus a whole-file checksum matching the current committed LFS object
- [ ] Warm-pixel counts per frame recorded and reconciled against Section 1.5 (19 frames non-zero, F14/F19/F22/F23/F24 zero)
- [ ] `sRGB`, `gAMA`, `pHYs`, `iTXt`, `caBX` chunk bytes recorded verbatim for later comparison
- [ ] Baseline artifacts live **outside** `Assets/` and Unity's console shows no new import activity as a result
- [ ] `Assets/Sprites/Quirrel_Sprites.png` and its `.meta` are byte-identical before and after this task

```
Phase 0 dependency graph:
  0.1 [ART]  (ART.md)            ─┐
  0.2 [QA]   (baseline capture)  ─┴──→ Phase 1
  (fully parallel — different files, no shared state)
```

---

## Phase 1 — The Recolor

All Phase 1 tasks operate on the same image and are **strictly sequential**. None may be
parallelised, regardless of the dependency graph.

#### Task 1.1: [ART] Build and verify the pin-head selection mask
**Depends on:** Task 0.2 (baseline frame inventory)
**Parallel:** **no** — first of the sequential image chain
**Touches:** none (new file, `Docs/Sprites/`, outside `Assets/`)
**Regression risk:** none directly, but this mask is the input that determines the blast radius
of Task 1.2. An over-broad mask here is how the text label or the mask/cloak gets recolored.

Produce a 1408×768 binary mask marking exactly the pin-head pixels, as the intersection of the
HSV color rule (Section 2.2) with per-frame regions from Section 1.5. Verify it frame by frame
against the measured inventory before it is used. Store the mask outside `Assets/` so it is not
imported, does not receive a GUID, and cannot be atlased or shipped.

**Acceptance criteria:**
- [ ] Mask marks pixels in exactly 19 frames; F14, F19, F22, F23, F24 contain zero marked pixels
- [ ] Per-frame marked-pixel counts reconcile with the Section 1.5 table within a stated tolerance, and any deviation is explained rather than accepted silently
- [ ] Both DEFEND collar regions are included (F20 x321–337, F21 second region), and the 17 px fragment at (534, 672) is included
- [ ] Both hue clusters are covered — marked pixels span hue 5–25° and 30–70°, verified by histogram
- [ ] Zero marked pixels fall inside any of the nine text-label bounding boxes; specifically (97, 21) and (95, 22) are **not** marked
- [ ] Zero marked pixels fall on the mask, cloak, body, legs, or pin shaft, confirmed by overlay review of all 19 frames
- [ ] Mask file is outside `Assets/`; Unity console shows no import of it
- [ ] `Assets/Sprites/Quirrel_Sprites.png` is byte-identical before and after this task

---

#### Task 1.2: [ART] Define and apply the Verdant color mapping (working copy)
**Depends on:** Task 1.1
**Parallel:** **no** — same image as 1.1/1.3/1.4
**Touches:** none yet (writes only the working copy in `Docs/Sprites/`)
**Regression risk:** **R1 (gamma/profile chunk loss) originates here.** The tool and export path
chosen in this task determine whether the whole sheet silently shifts brightness. Also the point
at which the cel-shading step count can be flattened.

Define the source→target transform anchored on `#6FA25C` (lit) and `#4A7A3E` (shadow), applying
hue shift **plus** the required desaturation and value reduction (Section 2.2), preserving
relative shading structure. Apply it to masked pixels only, writing an off-tree working copy.

**Acceptance criteria:**
- [ ] Lit-face pixels land on `#6FA25C` ± 3 per channel; shadow-face pixels land on `#4A7A3E` ± 3 per channel
- [ ] No output pixel in the head exceeds S 0.55 or V 0.70 — i.e. no fluorescent lime; conforms to ART.md 1 "high contrast, low chroma" and ART.md 8
- [ ] The head retains at least 2 distinguishable tone steps (ART.md 3); it is not a flat green fill
- [ ] Zero pixels outside the Task 1.1 mask differ from the baseline, verified by diff against Task 0.2 artifacts
- [ ] Output image is 1408×768, 8-bit RGBA, non-interlaced — unchanged from baseline
- [ ] Output retains `sRGB` intent 0 and `gAMA` 0.45455 **byte-identical** to baseline (R1)
- [ ] Working copy is outside `Assets/`; `Assets/Sprites/Quirrel_Sprites.png` is byte-identical before and after this task

---

#### Task 1.3: [ART] Anti-aliased boundary and fringe cleanup (working copy)
**Depends on:** Task 1.2
**Parallel:** **no** — same image
**Touches:** none yet (working copy only)
**Regression risk:** the classic failure of a hue-range recolor — a residual amber/orange halo at
every head/shaft and head/ink-line boundary, most visible on the largest heads (F20, F21) and at
gameplay scale on the ATTACK frames.

Resolve the ~1,884 boundary pixels between the strict and inclusive selections, plus the 85 sub-20 px
fringe fragments, so head-to-shaft and head-to-ink-line transitions blend to the new green rather
than through the old amber. Ink line stays `#1B1B1F`-family (ART.md 2.2 / 3) and is not recolored.

**Acceptance criteria:**
- [ ] No pixel with hue in 5–70° and S > 0.15 remains inside any pin-head region across all 19 frames
- [ ] At 4× magnification, no amber or orange halo is visible at any head boundary in F20 and F21 (the two largest heads)
- [ ] Ink outline surrounding the head remains in the `#1B1B1F` family, unrecolored, at original line weight
- [ ] Pin shaft retains `#C9CCD1` / `#9AA0AA` family values, unchanged from baseline (ART.md 2.3)
- [ ] Head silhouette shape is pixel-identical to baseline — this is a color-only change; the alpha channel is unchanged (still fully opaque)
- [ ] Zero pixels outside the pin-head regions differ from baseline
- [ ] `Assets/Sprites/Quirrel_Sprites.png` is byte-identical before and after this task

---

#### Task 1.4: [ART] Promote the approved working copy into `Assets/` in place
**Depends on:** Task 1.3
**Parallel:** **no** — the only task that writes the tracked asset
**Touches:** **`Assets/Sprites/Quirrel_Sprites.png`** (Git LFS binary, GUID `62b4e791ec637c245b6ea820090a2f94`)
**Regression risk:** **R2 (GUID loss).** The file must be overwritten in place. Deleting and
re-adding it, or letting Unity re-import it as a new asset, generates a new GUID and breaks every
future reference. Also permanently adds one ~0.9 MB object to LFS history — hence exactly one
write, not one per iteration.

**Acceptance criteria:**
- [ ] The PNG is overwritten in place; the file is never deleted from the working tree or from the index
- [ ] `Quirrel_Sprites.png.meta` is byte-identical to before, `guid: 62b4e791ec637c245b6ea820090a2f94` preserved
- [ ] `git status` shows exactly one modified file (`Assets/Sprites/Quirrel_Sprites.png`) and no deletion/rename
- [ ] The file is still LFS-tracked after the write (`git lfs ls-files` lists it with a new OID)
- [ ] Unity re-imports the texture with no console errors or warnings
- [ ] `Assets/Sprites/Reference/` is untouched — both reference PNGs and their metas byte-identical (R6)
- [ ] Exactly one new LFS object is created for this change

```
Phase 1 dependency graph (strictly serial — one image, one editor at a time):
  1.1 [ART] mask ──→ 1.2 [ART] remap ──→ 1.3 [ART] fringe ──→ 1.4 [ART] promote
  NO parallelism anywhere in this phase.
```

---

## Phase 2 — Verification

All Phase 2 tasks are **read-only** against the sprite sheet, so they may run concurrently with
each other. This is the one place in the plan where same-file parallelism is permitted, because
no task writes.

#### Task 2.1: [QA] Pixel-level diff against the Task 0.2 baseline
**Depends on:** Task 1.4
**Parallel:** yes — with Tasks 2.2 and 2.3
**Touches:** none (read-only)
**Regression risk:** this is the regression gate for R3 (over-selection) and R4 (remnants)

**Acceptance criteria:**
- [ ] All 19 pin-bearing frames show changed pixels; per-frame changed-pixel counts reconcile with Section 1.5 areas
- [ ] F14, F19, F22, F23, F24 are **byte-identical** to baseline — checksums match exactly
- [ ] Every changed pixel falls inside a Section 1.5 pin-head bounding box; zero changed pixels elsewhere
- [ ] All nine baked-in text labels are byte-identical to baseline, including (97, 21) and (95, 22)
- [ ] Quirrel's mask, cloak, body, legs and pin shaft are byte-identical to baseline in all 24 frames
- [ ] Whole-image alpha channel is unchanged (still 255 everywhere) — the recolor did not begin an accidental background knockout
- [ ] Image dimensions and bit depth unchanged: 1408×768, 8-bit RGBA

---

#### Task 2.2: [QA] Palette conformance and legibility validation
**Depends on:** Task 1.4
**Parallel:** yes — with Tasks 2.1 and 2.3
**Touches:** none (read-only)
**Regression risk:** this is the regression gate for R5 (contrast/legibility). ART.md 4.2's
point-vs-head distinction and ART.md 3's silhouette rule are the two art rules this recolor is
most able to break without looking obviously wrong in isolation.

**Acceptance criteria:**
- [ ] Sampled head colors across all 19 frames conform to ART.md 2.3 within the Task 1.2 tolerance; no frame drifts to a different green
- [ ] The 19 heads are mutually consistent — no visible hue or value drift between IDLE, WALK, JUMP, ATTACK and DEFEND heads
- [ ] Head-vs-shaft value separation is ≥ 0.15 in V, measured on F18 (extended point) and F21 (head forward), satisfying ART.md 4.2
- [ ] Greyscale silhouette pass on F15–F18 vs. F19–F21: attack-ready and block-ready poses remain distinguishable (ART.md 3)
- [ ] Head remains legible when composited against the ART.md 2.1 gameplay-floor range `#2B333E`–`#3B4652` and the teal midground range `#163542`–`#1E4A56`; any contrast shortfall against green/fungal biomes is documented as a finding rather than passed silently
- [ ] No infection-orange (`#FF7A3D` family) has been introduced anywhere (ART.md 2.6 / 8)
- [ ] Result does not read as "AI glow" or fluorescent (ART.md 8)

---

#### Task 2.3: [QA] Unity import and asset-integrity regression
**Depends on:** Task 1.4
**Parallel:** yes — with Tasks 2.1 and 2.2
**Touches:** none (read-only; opens the Unity Editor)
**Regression risk:** this is the regression gate for **R1 (gamma/profile chunk loss)** and
**R2 (GUID loss)** — the two failures that a visual review of the pin head cannot detect.

**Acceptance criteria:**
- [ ] PNG ancillary chunks byte-compare identical to the Task 0.2 record: `sRGB` intent 0, `gAMA` 0.45455, `pHYs`; any change to `iTXt`/`caBX` is explicitly reviewed and accepted rather than ignored
- [ ] Importer settings unchanged: `spriteMode: 1`, `sprites: []`, PPU 100, pivot (0.5, 0.5), `filterMode: 1`, maxTextureSize 2048, `sRGBTexture: 1`, `alphaIsTransparency: 1`, `ignorePngGamma: 0`
- [ ] `guid: 62b4e791ec637c245b6ea820090a2f94` unchanged in the `.meta`
- [ ] Unity re-imports with zero console errors and zero new warnings
- [ ] The imported texture preview shows no global brightness shift versus baseline on non-pin areas — sampled on Quirrel's mask (`#F2EEE3` family) and cloak (`#2E323D` family), which must be unchanged
- [ ] `SampleScene.unity` is unmodified

```
Phase 2 dependency graph:
  1.4 ──┬──→ 2.1 [QA] pixel diff        (R3, R4)
        ├──→ 2.2 [QA] palette/legibility (R5)
        └──→ 2.3 [QA] import integrity   (R1, R2)
  2.1 / 2.2 / 2.3 fully parallel — all read-only.
```

---

## Phase 3 — Close-out

#### Task 3.1: [ART] Close the ART.md gap and record the hand-off constraints
**Depends on:** Tasks 2.1, 2.2, 2.3 (all must pass)
**Parallel:** no — last task
**Touches:** `ART.md` (Section 9); adds a provenance note under `Assets/Sprites/Reference/`
**Regression risk:** low. Stale Section 9 entries cause a future agent to redo or undo this work
— which is exactly how the amber gets reintroduced.

Remove the "pin head color" gap from Section 9 and record Verdant as shipped in the base asset.
Add the note that `Assets/Sprites/Reference/` is deliberately *not* recolored (R6), and record
the two hand-off constraints for the future slicing/knockout plan.

**Acceptance criteria:**
- [ ] ART.md Section 9 bullet 1 replaced with a "closed" record naming the date and the F-IDs affected (19 of 24 frames)
- [ ] A note states that `Assets/Sprites/Reference/*` retains the original amber deliberately, as provenance, and must not be recolored or used to regenerate the working sheet
- [ ] Section 9 records that the slicing/knockout pass must re-run this plan's Task 2.1 diff afterwards
- [ ] Section 9 records the two slicing blockers found here — non-uniform frame layout with overlapping F17/F18 bounding boxes, and fully-opaque alpha requiring a knockout pass
- [ ] ART.md 2.3 hex values are unchanged; the Ember tier remains `#E8A33D` and is not confused with the removed amber
- [ ] No other ART.md section modified (diff confined to Section 9)

```
Phase 3 dependency graph:
  2.1 ──┐
  2.2 ──┼──→ 3.1 [ART] close ART.md Section 9
  2.3 ──┘
```

---

## 5. Full Task Summary

| # | Tag | Task | Depends on | Parallel | Est. |
|---|---|---|---|---|---|
| 0.1 | `[ART]` | Correct ART.md frame map | none | with 0.2 | 2h |
| 0.2 | `[QA]` | Capture pre-change baseline | none | with 0.1 | 2h |
| 1.1 | `[ART]` | Build/verify pin-head mask | 0.2 | no | 3h |
| 1.2 | `[ART]` | Apply Verdant mapping (working copy) | 1.1 | no | 3h |
| 1.3 | `[ART]` | Fringe/anti-alias cleanup | 1.2 | no | 3h |
| 1.4 | `[ART]` | Promote into `Assets/` in place | 1.3 | no | 1h |
| 2.1 | `[QA]` | Pixel diff vs. baseline | 1.4 | with 2.2, 2.3 | 2h |
| 2.2 | `[QA]` | Palette/legibility validation | 1.4 | with 2.1, 2.3 | 2h |
| 2.3 | `[QA]` | Unity import integrity | 1.4 | with 2.1, 2.2 | 1h |
| 3.1 | `[ART]` | Close ART.md Section 9 | 2.1, 2.2, 2.3 | no | 1h |

Critical path: 0.2 → 1.1 → 1.2 → 1.3 → 1.4 → (2.1‖2.2‖2.3) → 3.1 ≈ **14 hours**.

---

## 6. Discovered Work — Route Separately Through the Pipeline

Found while researching this plan. **Explicitly out of scope**; each needs its own plan per
CLAUDE.md, and none blocks this recolor.

1. **ART.md Section 9 bullet 2 is factually wrong.** No orange impact flash exists in the DIE
   frames; the HURT motes are neutral grey. The bullet should be withdrawn rather than acted on
   — a well-meaning agent could otherwise "fix" a non-existent problem and alter clean art.
2. **DIE frames do not contain the mask shatter** that ART.md 2.5 stage 4 and 4.1 describe. Mask
   crack stages 1–3 and the shatter are all unauthored. The health-feedback system in ART.md 2.5
   has no art behind it beyond stage 0.
3. **Background knockout** — the sheet is 100% opaque. Required before any frame is usable
   in-engine, independent of slicing.
4. **Slicing plan needs rescoping** — uniform grid slicing is impossible (Section 3); automatic
   slicing would emit nine text labels as sprites; F17/F18 bounding boxes overlap.
5. **Frame counts fall short of the ART.md 7 animation targets** for WALK (6 vs 8), JUMP (3 vs 4),
   ATTACK (4 vs 6), DEFEND (3 vs 4), HURT (1 vs 2) and DIE (3 vs 6). Either the art needs
   extending or the targets need revising — a design decision, not an art-pass decision.

---

## 7. Open Questions for the Reviewer

1. **Working-copy location.** `Docs/Sprites/` is proposed (exists, empty, outside `Assets/`).
   Confirm, or nominate an alternative — the requirement is only that it is not under `Assets/`.
2. **Retain the selection mask after completion?** It is a necessary intermediate either way; the
   question is whether it is committed for future pin-tier work or discarded. Recommendation:
   commit it under `Docs/Sprites/`, since regenerating it later costs a repeat of Task 1.1.
3. **Is Task 0.1 (ART.md frame-map correction) in scope here, or split out?** Argument for
   keeping it: this plan's acceptance criteria cannot cite frame IDs that do not exist, so
   something must fix the map first. Argument for splitting: it is a doc task with its own
   cross-discipline blast radius. Included here as the minimum viable correction.
4. **Should Task 1.4 be deferred behind a visual approval gate?** As written, promotion happens
   before Phase 2 verification runs. The alternative is approval on the working copy first, at
   the cost of a slower loop. Current sequencing is defensible because a bad promotion is a
   one-command LFS revert with zero downstream references today.
