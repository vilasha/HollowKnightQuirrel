# Implementation Plan — Pin Head Recolor: Gold/Amber → "Verdant" Green

**Status:** ⚪ SUPERSEDED — closed 2026-08-02 in favor of a direct manual edit by the user.

**Why:** two rounds of architect/reviewer cycles on this plan consumed disproportionate token
budget for what is, in practice, a single-image color edit with no code/system regression surface
(see `CLAUDE.md`, "Delegate to the human when that's cheaper"). The research in Sections 1–4 below
remains accurate and useful reference (verified frame map, verified color clusters, verified risks
R1/R2/R7) — it is the multi-round planning/verification *process* that was disproportionate, not
the findings. If this sprite sheet is ever regenerated or substantially reworked, this plan's
Section 1 measurements are a good starting reference, but should be re-verified rather than assumed.

**What actually happened instead:** the user recolored the pin head directly in an image editor.
See `Docs/Plans/002...` (if created) or the git history around this date for what changed, rather
than trusting this plan's Section 5 task list as a record of execution — it was never executed.. Round 1 findings (C3, M1–M7, m1–m5, Q1–Q4)
applied 2026-08-02; C1 and C2 handled per the routing-agent corrections recorded in Section 8.
Do NOT begin implementation until the reviewer marks this ✅ APPROVED.
**Author:** implementation-plan-architect
**Date:** 2026-08-02 (revised, round 2)
**Feature:** Recolor Quirrel's pin weapon head from gold/amber to the canonical Verdant green
(`#6FA25C` / `#4A7A3E`) across `Assets/Sprites/Quirrel_Sprites.png`
**Closes:** ART.md Section 9, bullet 1 ("Pin head color")
**Estimated total:** ~20 hours across 10 tasks (critical path 16h — see Section 5)

---

## 0. Executive Summary

This is a color-only correction to a single 1408×768 PNG, affecting **~8,459 pixels (0.78% of
the image)** across **19 of the sheet's 24 frames**. On paper it is trivial.

It is planned carefully anyway, for four reasons specific to this repo:

1. **It is a destructive, in-place edit to the only character asset the game has.** There is no
   second copy of a recolored sheet to fall back to, no prefab to revert, and the file is stored
   in Git LFS — so a bad edit is not visible in a text diff during review. It is also not truly
   revertible: CLAUDE.md step 4 commits and pushes, and a pushed LFS object is permanent storage
   even after the working tree is reverted. **Every byte written into `Assets/` must therefore be
   verified before it is written, not after** (Section 2.4).
2. **The sheet does not match what ART.md says it contains** (Section 1.3 below). Acceptance
   criteria written against ART.md's stated frame map would be unverifiable, because the frame
   indices that ART.md cites do not exist. That has to be corrected first or this task cannot be
   signed off.
3. **A PNG re-save can silently shift every pixel in the sheet**, not just the 0.78% being
   targeted, via PNG gamma/color-profile chunks that Unity's importer honours (Section 4.1).
   This is the single highest-severity risk in the task and it is invisible to a visual check of
   the pin head alone.
4. **What ships is not the source pixels but a block-compressed texture** (Section 1.3). The
   resolved format is chosen by Unity, is not recorded in the `.meta`, and re-quantises a small
   high-chroma region — so a recolor that is byte-perfect in the PNG can still band in the build
   (risk R7, Section 4.7).

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

**Compression / platform settings** (read from the same `.meta`, `platformSettings` block — these
matter because block compression is what turns a hue change into visible banding):

| Platform entry | maxTextureSize | textureFormat | textureCompression | compressionQuality | crunched | overridden |
|---|---|---|---|---|---|---|
| `DefaultTexturePlatform` | 2048 | `-1` (Automatic) | `1` (Normal) | 50 | 0 | 0 |
| `Standalone` | 2048 | `-1` (Automatic) | `1` (Normal) | 50 | 0 | 0 |
| `Android` | 2048 | `-1` (Automatic) | `1` (Normal) | 50 | 0 | 0 |

`textureFormat: -1` means the *authored* value is "Automatic"; the **resolved** runtime format is
decided by Unity per platform and is **not** stored in the `.meta`. It must be read off the
Inspector's platform tab, which is why Task 0.2 records it and Task 2.3 re-checks it. Automatic +
Normal quality on Standalone resolves to a block-compressed format (DXT/BC family) for an opaque
RGBA source — meaning the pin head, which is 160–1,167 px of saturated color inside 4×4
compression blocks, is exactly the kind of small high-chroma region whose artifacts change when
its hue and saturation change. See risk R7 (Section 4.7).

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

**Coordinate convention (used by every table in this plan, and required by Task 0.1 to be stated
in ART.md).** All (x, y) pairs here are **image-space pixel coordinates with the origin at the
top-left**, x increasing right, y increasing **downward** — the convention every image tool and
every measurement in this plan uses. Unity's `spriteSheet` rects use the **opposite** y direction:
origin bottom-left, y increasing upward. For this 768 px-tall sheet the conversion is:

```
y_unity_rect_bottom = 768 - y_image_bottom
y_unity_rect_top    = 768 - y_image_top
rect.y              = 768 - y_image_bottom          (rect.y is the bottom edge in Unity)
rect.height         = y_image_bottom - y_image_top
```

A frame whose image-space bbox is y 41–172 therefore becomes `rect.y = 596`, `rect.height = 131`.
Getting this backwards is the single most likely way for a future slicing pass to author rects
that are vertically mirrored across the sheet, so it is written down once, here and in ART.md.

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

### 2.3 Working off-tree, then a single verified promotion

All iteration happens on a **working copy outside `Assets/`**. Only Task 1.4 writes to the tracked
asset, once, after Tasks 2.1 and 2.2 have passed against the working copy and the contact sheet
has been signed off (Section 2.4).

**Directory policy — settled in round 1 review (Q1/Q2), two locations with different rules:**

| Location | Contents | Git |
|---|---|---|
| `Docs/Sprites/work/` | Every iteration artifact: baseline crops, intermediate masks, working-copy PNGs, contact sheets, overlay reviews | **Gitignored.** Never committed, at any point |
| `Docs/Sprites/` (root) | Exactly the durable artifacts: `pin-head-mask.png` (final), `README.md`, and the plain-text records (checksums, chunk dumps, histograms) | Committed **once, at close-out**, by Task 3.1 |

Neither directory currently exists in Git — `Docs/Sprites/` is present in the local working tree
but empty, and **Git does not track empty directories**, so nothing about it can be assumed. Task
0.2 creates both directories explicitly and adds `Docs/Sprites/work/` to `.gitignore` before
writing anything into them.

**Why the gitignore entry is mandatory rather than a convention (M5):** `.gitattributes` line 131
is `*.png lfs`, which is **repo-wide, not scoped to `Assets/`**. Any PNG anywhere in this repo that
gets staged becomes a permanent Git LFS object. A single reflexive `git add -A` during iteration
would push a dozen ~0.9 MB half-remapped intermediates into LFS history, where they cannot be
removed without a history rewrite. Convention does not survive that; a `.gitignore` entry does.

The same rule is what makes the durable mask a deliberate decision rather than an accident: the
committed `Docs/Sprites/pin-head-mask.png` **will** be an LFS object. It is written as a minimal
1-bit or 8-bit greyscale PNG with no ancillary chunks precisely to keep that object small, and it
is committed exactly once. No intermediate mask is ever committed.

Rationale for working off-tree at all:
- Files placed under `Assets/` are imported by Unity, get a `.meta` and a new GUID, and are
  candidates for atlasing and for inclusion in a build. Iteration artifacts must not become
  game assets by accident. (This is also why Task 3.1 writes its provenance note to
  `Docs/Sprites/README.md` and ART.md, and **not** into `Assets/Sprites/Reference/`.)
- One write to the tracked PNG means **one** new Git LFS object rather than one per iteration
  (each is ~0.9 MB, permanent in LFS history).
- A fringed or half-remapped intermediate is never committed to `Assets/`, so `main` is never in
  a state where the character's weapon is visibly broken.

### 2.4 Promotion is gated on verification, not followed by it

Round 1 sequenced promotion (Task 1.4) before all verification, on the argument that a bad
promotion is a one-command LFS revert. That argument holds for the *working tree* but not for
*LFS history*, and CLAUDE.md step 4 commits and pushes — so a bad promotion is permanent storage,
not a revert. The order is therefore:

1. Tasks 2.1 (pixel diff) and 2.2 (palette/legibility + contact sheet) run **against the working
   copy** in `Docs/Sprites/work/`, in parallel, immediately after Task 1.3.
2. The Task 2.2 contact sheet is signed off, and the sign-off is **recorded in this plan file**
   (Section 5.1) before any byte is written to `Assets/`.
3. Task 1.4 promotes, and asserts **SHA-256 equality** between the promoted file and the exact
   working copy that passed 2.1/2.2. That equality is what makes re-running 2.1/2.2 after
   promotion unnecessary — the verified artifact and the promoted artifact are provably the same
   bytes.
4. Task 2.3 runs after promotion, because it is the only check that requires the file to be inside
   `Assets/` (Unity import, GUID, `.meta`, resolved compression format).

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
gold. Task 3.1 records this **in ART.md Section 9 and `Docs/Sprites/README.md`** — not as a file
inside `Assets/Sprites/Reference/`, because anything placed there becomes an imported Unity asset
with its own GUID (C3, Section 2.3).

### 4.7 R7 — Compressed-format banding and platform drift (severity: medium, likelihood: medium)

The importer authors `textureFormat: -1` (Automatic) with `textureCompression: 1` (Normal) on the
Default, Standalone and Android platform entries (Section 1.3). The *resolved* format is chosen by
Unity and is not recorded in the `.meta`, so a change in it is invisible to a `.meta` diff.

Two distinct failure modes:

- **Banding.** Block compression quantises color inside 4×4 blocks. The pin head is a small,
  high-chroma, cel-shaded region — 160 px on F15 up to 1,167 px on F21 — sitting against a dark
  ink outline. Moving it from amber (V 0.886) to Verdant (V 0.635 / 0.478) changes both the
  chroma and the intra-block contrast, so the compression artifacts on the head are **not
  guaranteed to be the same** even though the uncompressed pixels are correct. A recolor that is
  perfect in the source PNG can still band or blotch in the compressed preview.
- **Platform drift.** Nothing in this repo pins the resolved format. If it differs between
  Standalone targets, the head can look correct on the authoring machine and wrong in a build.

**Mitigation:** Task 0.2 records the resolved compressed format from the Inspector's platform tabs
(Default and Standalone) *before* the edit. Task 2.3 asserts the Standalone-tab format is
unchanged and inspects the compressed preview at 4× for new banding on the head. Because this
project has no scene rendering the sprite yet, the in-build visual check cannot be performed now;
Task 3.1 records in ART.md Section 9 that the **first scene to render this sprite must include a
Windows and a Linux Standalone visual check of the pin head**, so the obligation survives this
plan rather than being silently dropped.

### 4.8 Systems explicitly NOT at risk

Confirmed by inspection, so the QA pass need not scope them: no scripts, no asmdefs, no prefabs,
no AnimationClips, no AnimatorControllers, no ScriptableObjects, no save schema, no persisted
progression state, no ability gating, no room transitions, no atlases, no build scripts. No
`[DATA]` migration task is required. `SampleScene.unity` does not reference the sprite.

**Complete write list for the whole plan** — anything outside this list is a defect:

| Path | Written by | Notes |
|---|---|---|
| `ART.md` | 0.1, 3.1 | Sections 2.5, 4.1, 4.2, 7, 9 only; LF preserved |
| `.gitignore` | 0.2 | One added entry, `Docs/Sprites/work/` |
| `Docs/Sprites/work/**` | 0.2, 1.1, 1.2, 1.3, 2.2 | Gitignored; never committed |
| `Assets/Sprites/Quirrel_Sprites.png` | **1.4 only** | One in-place overwrite, hash-bound |
| `Docs/Sprites/pin-head-mask.png`, `Docs/Sprites/README.md`, text records | 3.1 | Committed once at close-out |
| `Docs/Plans/001_pin-head-recolor.md` (Section 5.1) | 0.2, 1.3, 2.1, 2.2, 1.4, 2.3 | The verification ledger |

No task other than 1.4 writes anything under `Assets/`, at any point, for any reason.

---

## Phase 0 — Baseline and Guardrails

#### Task 0.1: [ART] Correct the ART.md frame map to match the actual sheet
**Depends on:** none
**Parallel:** yes — with Task 0.2
**Touches:** `ART.md` (Sections 2.5, 4.1, 4.2, 7, and the stale indices in Section 9 only)
**Regression risk:** ART.md is the project's single source of truth and is cited by five other
disciplines (Section 10). Correcting it changes numbers other agents may already have read.
Frame *indices* change meaning, so any in-flight work citing "frames 17–22" must be re-pointed.

**Scope is deliberately narrow (round 1, Q3): frame indices, frame counts and the coordinate
convention only. No design or content claim in ART.md is added, removed or reworded by this task**
— in particular, ART.md Section 9 bullet 2's claim that an orange impact flash exists is *not*
withdrawn here (that is Section 6 item 1, routed separately); only its dead frame number is
re-pointed.

Replace the Section 7 animation-timing frame counts with the measured values, and replace the
non-existent frame-index citations in Sections 2.5, 4.1, 4.2 and 9 with the canonical F1–F24 IDs
from Section 1.5 of this plan. Add the F1–F24 frame map **into Section 7**, immediately after the
animation-timing table, as the one authoritative index scheme (m4) — not Section 4 and not a new
top-level section, because Section 7 is where Sections 2.5, 4.1 and 4.2 already point for pipeline
facts and where @gameplay-programmer is directed by Section 10. Record that the counts describe
*the current asset*, and that ART.md's previous counts should be read as the *target* for future
re-authored art where they differ.

The two stale index citations in Section 9 (M1) are: bullet 2's "**frame 29** of
`Quirrel_Sprites.png` (start of the DIE sequence)" → **F22**, and bullet 4's "**DIE frames
29–34**" → **F22–F24**. Task 3.1 re-verifies that none survive.

**Line-ending constraint (m3):** `.gitattributes` gives `*.md` no `text` attribute, so Git does
not normalise it. `ART.md` is currently **LF-only, no CR bytes, UTF-8, no BOM** (verified). Any
editor that rewrites the file as CRLF will make every "diff confined to Section X" criterion in
this plan fail spuriously by reporting the whole file as changed. Preserve LF.

**Acceptance criteria:**
- [ ] ART.md Section 7 timing table states IDLE 4 / WALK 6 / JUMP 3 / HURT 1 / ATTACK 4 / DEFEND 3 / DIE 3, total 24
- [ ] Where a count is a future target rather than current reality, it is labelled as such and not silently overwritten
- [ ] Section 2.5 stage-4 citation no longer references "frames 29–34"; it references F22–F24 and notes that mask-shatter art does not yet exist
- [ ] Section 4.1 no longer references "frames 29–34"; Section 4.2 no longer references "frames 17–22" (uses F15–F18) and DEFEND uses F20–F21
- [ ] Section 9 bullet 2 reads "F22" instead of "frame 29", and bullet 4 reads "F22–F24" instead of "DIE frames 29–34"; **the wording of both claims is otherwise unchanged**
- [ ] A grep of ART.md for the standalone frame numbers `29`, `29–34`, `29-34`, `17–22`, `17-22` returns zero frame-index hits
- [ ] The canonical F1–F24 frame-ID table, with per-frame bounding boxes, is present **in Section 7**
- [ ] The frame table states the coordinate origin explicitly — **top-left, image space, y increasing downward** — and gives the Unity conversion `rect.y = 768 - y_image_bottom`, `rect.height = y_image_bottom - y_image_top`, with one worked example (M4)
- [ ] No other ART.md section is edited — palette, pillars and guards are untouched (regression check: diff shows changes confined to Sections 2.5, 4.1, 4.2, 7 and the two Section 9 index tokens)
- [ ] Every hex value in ART.md is unchanged (`#6FA25C` and `#4A7A3E` in particular)
- [ ] `ART.md` remains LF-only with no BOM after the edit; `git diff --stat` shows a small changed-line count, not a whole-file rewrite (m3)

---

#### Task 0.2: [QA] Set up the off-tree workspace and capture the pre-change baseline
**Depends on:** none
**Parallel:** yes — with Task 0.1
**Touches:** `.gitignore` (one added entry); creates `Docs/Sprites/` and `Docs/Sprites/work/`.
Read-only against everything under `Assets/`
**Regression risk:** low. The `.gitignore` edit is additive and scoped to a path that holds no
tracked files today — but it must be verified not to shadow anything under `Assets/`, since a
mis-typed pattern that ignores art assets is silent and destructive at commit time.

This is the characterization step in front of a destructive, LFS-stored, visually-reviewed edit.
Without it, "did anything else change?" is unanswerable after the fact.

**First, create the workspace (m2 — `Docs/Sprites/` is untracked and empty; Git does not track
empty directories, so its existence cannot be assumed):**

- Create `Docs/Sprites/` and `Docs/Sprites/work/`.
- Add `Docs/Sprites/work/` to `.gitignore` **before** writing any file into it, for the
  repo-wide-`*.png lfs` reason in Section 2.3.
- Everything this task produces goes under `Docs/Sprites/work/baseline/`. Nothing produced by
  Phase 0, 1 or 2 is ever staged; only Task 3.1 commits, and only from `Docs/Sprites/` root.

**Then capture the baseline:** a per-frame crop set for all 24 frames using the Section 1.5
bounding boxes; a full-image SHA-256; a per-frame checksum set; a color inventory (distinct color
count, warm-pixel count per frame, hue histogram); the PNG ancillary chunk list with byte
contents, so R1 can be checked later; and the resolved compressed texture format from Unity's
Inspector (R7/M3).

**Per-head color characterization (round 1, C1 — downgraded to Major but still required).** For
each of the 19 pin-bearing frames, record the head region's **hue/S/V histogram and its modal
value per cluster** (red-orange cluster and gold cluster separately, per Section 1.6). This is the
measurement that tells Task 1.1 whether the two clusters are *intra-head shading* (one transform
for the whole sheet) or *inter-frame drift* (frames genuinely painted at different hues, which
would force a per-frame or normalising transform). A visual read suggests intra-head shading with
DEFEND merely showing the disc face-on; this task produces the number that settles it.

**Acceptance criteria:**
- [ ] `Docs/Sprites/` and `Docs/Sprites/work/` exist; `Docs/Sprites/work/` is listed in `.gitignore`
- [ ] `git check-ignore -v Docs/Sprites/work/test.png` reports the new rule; `git check-ignore Assets/Sprites/Quirrel_Sprites.png` reports **nothing** (the new rule shadows no asset)
- [ ] `git status --porcelain` after this task lists **only** the `.gitignore` modification — no untracked baseline artifacts, no untracked PNGs anywhere
- [ ] 24 per-frame crops exist under `Docs/Sprites/work/baseline/` and are visually confirmed to contain one frame each
- [ ] Per-frame checksums recorded for all 24 frames, plus a whole-file SHA-256 and the current LFS OID (`git lfs ls-files -l`) of `Assets/Sprites/Quirrel_Sprites.png`
- [ ] Warm-pixel counts per frame recorded and reconciled against Section 1.5 (19 frames non-zero, F14/F19/F22/F23/F24 zero)
- [ ] For each of the 19 pin-bearing frames: hue/S/V histogram plus modal H, S and V recorded **per cluster** (red-orange 5–25°, gold 30–55°, yellow 55–70°), in one table (C1)
- [ ] The spread of per-frame modal hue is stated as a single number per cluster (max modal H − min modal H across the 19 frames), so Task 1.1 has a threshold to judge against
- [ ] `sRGB`, `gAMA`, `pHYs`, `iTXt`, `caBX` chunk bytes recorded verbatim for later comparison
- [ ] The **resolved** compressed texture format is recorded from the Inspector for both the Default tab and the Standalone tab, together with the reported runtime memory size (M3/R7)
- [ ] Baseline artifacts live **outside** `Assets/` and Unity's console shows no new import activity as a result
- [ ] `Assets/Sprites/Quirrel_Sprites.png` and its `.meta` are byte-identical before and after this task (SHA-256 compared, not eyeballed)

```
Phase 0 dependency graph:
  0.1 [ART]  (ART.md)                          ─┐
  0.2 [QA]   (.gitignore + workspace + baseline)─┴──→ Phase 1
  (fully parallel — different files, no shared state:
   0.1 writes ART.md only, 0.2 writes .gitignore and Docs/Sprites/work/ only)
```

---

## Phase 1 — The Recolor (working copy only — nothing under `Assets/` is written in this phase)

All Phase 1 tasks operate on the same image and are **strictly sequential**. None may be
parallelised, regardless of the dependency graph. Every artifact lands in `Docs/Sprites/work/`.

#### Task 1.1: [ART] Build and verify the pin-head selection mask
**Depends on:** Task 0.2 (baseline frame inventory and per-head cluster modals)
**Parallel:** **no** — first of the sequential image chain
**Touches:** none (new file under `Docs/Sprites/work/`, outside `Assets/`)
**Regression risk:** none directly, but this mask is the input that determines the blast radius
of Task 1.2. An over-broad mask here is how the text label or the mask/cloak gets recolored.

Produce a 1408×768 binary mask marking exactly the pin-head pixels, as the intersection of the
HSV color rule (Section 2.2) with per-frame regions from Section 1.5. Verify it frame by frame
against the measured inventory before it is used. Store the mask in `Docs/Sprites/work/` so it is
not imported, does not receive a GUID, cannot be atlased or shipped, and cannot be staged.

**Also decide, on Task 0.2's data, whether the two hue clusters are intra-head or inter-frame
(C1).** This is a determination, not an implementation: state in writing whether the red-orange
and gold clusters represent shading *within* each head (expected — one global transform is
correct) or genuine hue drift *between* frames (would require a normalising or per-frame
transform). Task 1.2 must not choose a transform before this is written down. Decision rule:
if the spread of per-frame modal hue within a cluster is **≤ 8°**, treat it as intra-head shading
and use the single continuous transform; if any frame's cluster modal sits **> 8°** from the
19-frame median, name that frame and state how Task 1.2 handles it. The default, absent contrary
data, is the continuous hue-preserving transform in Section 2.2 — the burden is on the data to
justify anything more complicated.

**Acceptance criteria:**
- [ ] A written intra-head-vs-inter-frame determination exists, citing the Task 0.2 per-frame modal table and the ≤ 8° threshold, and names any outlier frame (C1)
- [ ] Mask marks pixels in exactly 19 frames; F14, F19, F22, F23, F24 contain zero marked pixels
- [ ] Per-frame marked-pixel counts reconcile with the Section 1.5 table within a stated tolerance, and any deviation is explained rather than accepted silently
- [ ] Both DEFEND collar regions are included (F20 x321–337, F21 second region), and the 17 px fragment at (534, 672) is included
- [ ] Both hue clusters are covered — marked pixels span hue 5–25° and 30–70°, verified by histogram
- [ ] Zero marked pixels fall inside any of the nine text-label bounding boxes; specifically (97, 21) and (95, 22) are **not** marked
- [ ] Zero marked pixels fall on the mask, cloak, body, legs, or pin shaft, confirmed by overlay review of all 19 frames
- [ ] Mask file is in `Docs/Sprites/work/`, is covered by the Task 0.2 gitignore rule (`git status --porcelain` shows it as neither tracked nor untracked), and Unity's console shows no import of it
- [ ] `Assets/Sprites/Quirrel_Sprites.png` is byte-identical (SHA-256) before and after this task

---

#### Task 1.2: [ART] Define and apply the Verdant color mapping (working copy)
**Depends on:** Task 1.1 (mask **and** its intra-head-vs-inter-frame determination)
**Parallel:** **no** — same image as 1.1/1.3/1.4
**Touches:** none yet (writes only the working copy in `Docs/Sprites/work/`)
**Regression risk:** **R1 (gamma/profile chunk loss) originates here.** The tool and export path
chosen in this task determine whether the whole sheet silently shifts brightness. Also the point
at which the cel-shading step count can be flattened.

Define the source→target transform anchored on `#6FA25C` (lit) and `#4A7A3E` (shadow), applying
hue shift **plus** the required desaturation and value reduction (Section 2.2), preserving
relative shading structure. Apply it to masked pixels only, writing an off-tree working copy to
`Docs/Sprites/work/`.

**How the color tolerance is measured (M6).** "±3 per channel" is only meaningful against a
defined population, because anti-aliased boundary pixels legitimately land *between* the two
anchors and would fail any strict per-pixel test. The population is therefore split, using the
Task 1.1 mask:

- **Non-boundary pixels** = masked pixels whose 8-neighbourhood is entirely inside the mask.
  These are the head's interior and are the ones the anchors apply to.
- **Boundary pixels** = masked pixels with at least one unmasked neighbour. These are exempt from
  the anchor test and are governed by Task 1.3 instead.

The test is run against the **cluster modal output value** (the most frequent output value in the
lit cluster, and in the shadow cluster), not against every pixel individually.

**Acceptance criteria:**
- [ ] The lit-cluster **modal** output value equals `#6FA25C` ± 3 per channel; the shadow-cluster modal output value equals `#4A7A3E` ± 3 per channel (M6)
- [ ] **≥ 95%** of non-boundary lit-cluster pixels fall within ± 8 per channel of the lit modal value, and ≥ 95% of non-boundary shadow-cluster pixels within ± 8 per channel of the shadow modal value — i.e. the interior is coherent, not speckled (M6)
- [ ] Boundary-pixel count is reported separately and is not counted as a failure of the two criteria above
- [ ] No output pixel in the head exceeds S 0.55 or V 0.70 — i.e. no fluorescent lime; conforms to ART.md 1 "high contrast, low chroma" and ART.md 8
- [ ] The head retains at least 2 distinguishable tone steps (ART.md 3), where "distinguishable" = the two cluster modal values differ by ≥ 0.10 in V; it is not a flat green fill
- [ ] The transform used matches the Task 1.1 determination (single continuous transform unless that task recorded an outlier and prescribed handling for it)
- [ ] Zero pixels outside the Task 1.1 mask differ from the baseline, verified by diff against Task 0.2 artifacts
- [ ] Output image is 1408×768, 8-bit RGBA, non-interlaced — unchanged from baseline
- [ ] Output retains `sRGB` intent 0 and `gAMA` 0.45455 **byte-identical** to baseline (R1)
- [ ] Working copy is in `Docs/Sprites/work/`; `Assets/Sprites/Quirrel_Sprites.png` is byte-identical (SHA-256) before and after this task

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
- [ ] The final post-fringe mask is exported once as a minimal 1-bit or 8-bit greyscale PNG with **no ancillary chunks**, to `Docs/Sprites/work/pin-head-mask.png` — this is the candidate Task 3.1 promotes to `Docs/Sprites/` and commits (Q2). It is **not** staged by this task
- [ ] The working copy that exits this task has its SHA-256 recorded in this plan file (Section 5.1); that hash is what Tasks 2.1/2.2 verify and what Task 1.4 must reproduce byte-for-byte
- [ ] `Assets/Sprites/Quirrel_Sprites.png` is byte-identical (SHA-256) before and after this task

```
Phase 1 dependency graph (strictly serial — one image, one editor at a time):
  1.1 [ART] mask ──→ 1.2 [ART] remap ──→ 1.3 [ART] fringe
  NO parallelism anywhere in this phase. Nothing under Assets/ is written.
  Exit artifact: an approved-pending working copy in Docs/Sprites/work/ with a recorded SHA-256.
```

---

## Phase 2 — Verification, then Promotion

Restructured in round 2 per review answer Q4. Verification now runs **before** promotion, against
the working copy, and the promotion is bound to the verified bytes by a hash equality check.

Task numbers are carried over from round 1 for continuity with the review record; the phase is
listed in **execution order**, which means Task 1.4 appears here, between 2.2 and 2.3, rather than
at the end of Phase 1.

Execution order: **2.1 ‖ 2.2 → (sign-off) → 1.4 → 2.3**.

Tasks 2.1 and 2.2 are read-only against the working copy, so they may run concurrently with each
other — this is the one place in the plan where same-file parallelism is permitted, because
neither writes. Task 1.4 is the only task in the entire plan that writes to `Assets/`.

#### Task 2.1: [QA] Pixel-level diff of the working copy against the Task 0.2 baseline
**Depends on:** Task 1.3 (runs against the working copy in `Docs/Sprites/work/`, **before** promotion)
**Parallel:** yes — with Task 2.2 only (**not** with 2.3, which cannot start until after 1.4)
**Touches:** none (read-only; does not touch `Assets/` at all)
**Regression risk:** this is the regression gate for R3 (over-selection) and R4 (remnants), and it
is now a **gate on promotion** rather than a post-mortem of one

**Acceptance criteria:**
- [ ] The file under test is identified by SHA-256, and that hash matches the working-copy hash recorded by Task 1.3
- [ ] All 19 pin-bearing frames show changed pixels; per-frame changed-pixel counts reconcile with Section 1.5 areas
- [ ] F14, F19, F22, F23, F24 are **byte-identical** to baseline — checksums match exactly
- [ ] Every changed pixel falls inside a Section 1.5 pin-head bounding box; zero changed pixels elsewhere
- [ ] All nine baked-in text labels are byte-identical to baseline, including (97, 21) and (95, 22)
- [ ] Quirrel's mask, cloak, body, legs and pin shaft are byte-identical to baseline in all 24 frames
- [ ] Whole-image alpha channel is unchanged (still 255 everywhere) — the recolor did not begin an accidental background knockout
- [ ] Image dimensions and bit depth unchanged: 1408×768, 8-bit RGBA
- [ ] Result (PASS/FAIL plus the tested SHA-256) is recorded in Section 5.1 of this plan file; a FAIL sends work back to Task 1.2/1.3 and **blocks Task 1.4**
- [ ] `Assets/Sprites/Quirrel_Sprites.png` is byte-identical before and after this task — it is still the original amber sheet at this point

---

#### Task 2.2: [QA] Palette conformance, legibility validation, and the approval contact sheet
**Depends on:** Task 1.3 (runs against the working copy in `Docs/Sprites/work/`, **before** promotion)
**Parallel:** yes — with Task 2.1 only (**not** with 2.3, which cannot start until after 1.4)
**Touches:** none (read-only against the sprite sheet; writes only into gitignored
`Docs/Sprites/work/`)
**Regression risk:** this is the regression gate for R5 (contrast/legibility), and it is now the
**promotion gate** — its contact sheet is the artifact a human signs off before 1.4. ART.md 4.2's
point-vs-head distinction and ART.md 3's silhouette rule are the two art rules this recolor is
most able to break without looking obviously wrong in isolation.

**Deliverable — the approval artifact (Q4).** A baseline-vs-recolored **contact sheet** covering
all 19 pin-bearing frames, at 1× and at 4×, written to `Docs/Sprites/work/`. Baseline crop and
recolored crop are placed side by side per frame and labelled with the F-ID. This is the artifact
a human signs off on in Section 5.1, and that sign-off is what unblocks Task 1.4. It is an
iteration artifact and stays gitignored; it is not promoted at close-out.

**Measurable restatements (M6).** "No visible drift" and "does not read as AI glow" are not
verifiable claims, so they are replaced by numbers:

- **Inter-frame consistency:** across the 19 per-frame **modal head colors**, max ΔH ≤ 5° and
  max ΔV ≤ 0.05 (measured as the spread between the highest and lowest per-frame modal, per
  cluster).
- **Background legibility:** ΔV ≥ 0.20 between the head's modal V and each of the ART.md 2.1
  ranges `#2B333E`–`#3B4652` (gameplay floor) and `#163542`–`#1E4A56` (teal midground),
  evaluated against the *closest* value in each range, i.e. the worst case.
- **"AI glow" criterion deleted** — the objective part of it is already covered by the S ≤ 0.55 /
  V ≤ 0.70 ceiling in Task 1.2 and by the infection-orange check below.

**Acceptance criteria:**
- [ ] The file under test is identified by SHA-256 and matches the working-copy hash recorded by Task 1.3
- [ ] Per-frame modal head color measured for all 19 frames and tabulated
- [ ] Inter-frame spread across those 19 modals is **ΔH ≤ 5°** and **ΔV ≤ 0.05**, per cluster (M6)
- [ ] Each per-frame modal is within the Task 1.2 anchor tolerance of `#6FA25C` (lit) / `#4A7A3E` (shadow) — no frame lands on a different green
- [ ] Head-vs-shaft value separation is ≥ 0.15 in V, measured on F18 (extended point) and F21 (head forward), satisfying ART.md 4.2
- [ ] Greyscale silhouette pass on F15–F18 vs. F19–F21: attack-ready and block-ready poses remain distinguishable (ART.md 3)
- [ ] Head modal V differs by **≥ 0.20** from the nearest value in `#2B333E`–`#3B4652` and from the nearest value in `#163542`–`#1E4A56` (M6); any shortfall is recorded as a finding with the measured number, not passed silently
- [ ] Contrast against a hypothetical green/fungal biome is recorded as a **documented forward risk** with its measured ΔH from `#6FA25C`, since no such biome art exists yet to test against
- [ ] No infection-orange (`#FF7A3D` family) has been introduced anywhere (ART.md 2.6 / 8)
- [ ] Contact sheet for all 19 frames at 1× and 4× exists in `Docs/Sprites/work/` and is gitignored
- [ ] Result (PASS/FAIL plus tested SHA-256) recorded in Section 5.1; a FAIL **blocks Task 1.4**
- [ ] `Assets/Sprites/Quirrel_Sprites.png` is byte-identical before and after this task

---

#### Task 1.4: [ART] Promote the verified working copy into `Assets/` in place
**Depends on:** **Tasks 2.1 and 2.2** (both PASS) **and** a recorded contact-sheet sign-off in
Section 5.1. Task 1.3 is an indirect dependency via those.
**Parallel:** **no** — the only task in the plan that writes the tracked asset
**Touches:** **`Assets/Sprites/Quirrel_Sprites.png`** (Git LFS binary, GUID `62b4e791ec637c245b6ea820090a2f94`)
**Regression risk:** **R2 (GUID loss).** The file must be overwritten in place. Deleting and
re-adding it, or letting Unity re-import it as a new asset, generates a new GUID and breaks every
future reference. Also permanently adds one ~0.9 MB object to LFS history that CLAUDE.md step 4
will push — hence exactly one write, of bytes that have already passed 2.1 and 2.2.

**Editor state (M7).** The **Unity Editor must be closed for the duration of this task.** Unity
holds and rewrites `.meta` files and its asset database while running; an in-place binary
overwrite under a live Editor can trigger a re-import mid-write, produce a truncated or partially
imported texture, or cause Unity to rewrite the `.meta` (the exact thing R2 guards against). The
sequence is: close the Editor → verify it is not running → overwrite → verify hashes and `git
status` → leave it closed. The Editor is opened **exactly once afterwards, by Task 2.3**, which is
the task that owns observing the re-import.

**Hash binding.** The promoted file must be the *same bytes* that passed verification. That is
asserted by SHA-256 equality, and it is what makes re-running 2.1/2.2 after promotion unnecessary.

**Acceptance criteria:**
- [ ] Section 5.1 contains, before the write: Task 2.1 PASS, Task 2.2 PASS, and a dated
      contact-sheet sign-off — all three naming the same working-copy SHA-256
- [ ] The Unity Editor was closed before the write and was not running during it (M7); it is **not** reopened by this task
- [ ] The PNG is overwritten in place; the file is never deleted from the working tree or from the index
- [ ] **SHA-256 of `Assets/Sprites/Quirrel_Sprites.png` after the write equals the SHA-256 of the signed-off working copy**, exactly (Q4)
- [ ] `Quirrel_Sprites.png.meta` is byte-identical to before, `guid: 62b4e791ec637c245b6ea820090a2f94` preserved (already tracked since commit `3672612` — no new commit of the `.meta` is required, only proof it did not change)
- [ ] `git status --porcelain` shows exactly one modified path under `Assets/` (`Assets/Sprites/Quirrel_Sprites.png`), no deletion, no rename, and no untracked files under `Docs/Sprites/work/`
- [ ] The file is still LFS-tracked after the write (`git lfs ls-files -l` lists it with a new OID); the new OID is recorded in Section 5.1 for the Task 3.1 README
- [ ] `Assets/Sprites/Reference/` is untouched — both reference PNGs and their metas byte-identical (R6)
- [ ] Exactly one new LFS object is created for this change

---

#### Task 2.3: [QA] Unity import, compression and asset-integrity regression
**Depends on:** **Task 1.4**
**Parallel:** **no** — it is the only task that opens the Unity Editor, and it runs alone after
promotion
**Touches:** none (read-only; opens the Unity Editor — the single reopen described in Task 1.4)
**Regression risk:** this is the regression gate for **R1 (gamma/profile chunk loss)**,
**R2 (GUID loss)** and **R7 (compressed-format banding / platform drift)** — the three failures
that a visual review of the source pixels cannot detect.

**Acceptance criteria:**
- [ ] The Unity Editor is opened here for the first time since Task 1.4's overwrite (M7), and the import that follows is the one being observed
- [ ] PNG ancillary chunks byte-compare identical to the Task 0.2 record: `sRGB` intent 0, `gAMA` 0.45455, `pHYs`; any change to `iTXt`/`caBX` is explicitly reviewed and accepted rather than ignored
- [ ] Importer settings unchanged: `spriteMode: 1`, `sprites: []`, PPU 100, pivot (0.5, 0.5), `filterMode: 1`, maxTextureSize 2048, `sRGBTexture: 1`, `alphaIsTransparency: 1`, `ignorePngGamma: 0`
- [ ] `guid: 62b4e791ec637c245b6ea820090a2f94` unchanged in the `.meta`; the `.meta` is byte-identical to its committed version
- [ ] **Standalone platform tab: the resolved compressed texture format is identical to the Task 0.2 record**, as is the reported runtime memory size; the Default tab likewise (M3/R7)
- [ ] `platformSettings` in the `.meta` still reads `textureFormat: -1`, `textureCompression: 1`, `compressionQuality: 50`, `crunchedCompression: 0`, `overridden: 0` for Default, Standalone and Android (M3)
- [ ] **Compressed preview inspected at 4× on F20 and F21** (largest heads, 677 px and 1,167 px) and on F15 (smallest head region, 160 px, where a single 4×4 block covers a larger share of the head): no new banding, blocking or blotching versus the baseline compressed preview (R7)
- [ ] Unity re-imports with zero console errors and zero new warnings
- [ ] The imported texture preview shows no global brightness shift versus baseline on non-pin areas — sampled on Quirrel's mask (`#F2EEE3` family) and cloak (`#2E323D` family), which must be unchanged
- [ ] `SampleScene.unity` is unmodified, and no new `.meta` files were generated anywhere in `Assets/`

```
Phase 2 dependency graph (Q4 — verification gates promotion):
  1.3 ──┬──→ 2.1 [QA] pixel diff vs baseline      (R3, R4)  ┐
        └──→ 2.2 [QA] palette/legibility + contact (R5)     ┤ both against the WORKING COPY
                                                            │ 2.1 ‖ 2.2 (read-only, parallel)
                                    (human sign-off in 5.1) ┘
                                             │
                                             ▼
                              1.4 [ART] promote in place  (R2; SHA-256 equality, Editor closed)
                                             │
                                             ▼
                              2.3 [QA] import + compression (R1, R2, R7; Editor opened once)

  2.3 is NOT parallel with 2.1/2.2 any more — it needs the file to be inside Assets/.
```

---

## Phase 3 — Close-out

#### Task 3.1: [ART] Close the ART.md gap, publish the durable artifacts, record the hand-offs
**Depends on:** Tasks 2.1, 2.2, 2.3 (all must pass) and Task 1.4 (for the new LFS OID)
**Parallel:** no — last task
**Touches:** `ART.md` (Section 9); creates `Docs/Sprites/README.md`; promotes
`Docs/Sprites/pin-head-mask.png`. **Writes nothing under `Assets/` (C3).**
**Regression risk:** low. Stale Section 9 entries cause a future agent to redo or undo this work
— which is exactly how the amber gets reintroduced. The one real risk is the mask PNG becoming an
oversized permanent LFS object; it is written minimal and committed once.

**C3 — the provenance note does not go into `Assets/`.** Round 1 placed a provenance note under
`Assets/Sprites/Reference/`, which contradicts this plan's own rule that the recolor adds **zero
new assets to `Assets/`** (Sections 2.1 and 2.3): any file dropped there is imported by Unity,
receives a `.meta` and a GUID, and becomes a build candidate. The note therefore lives in exactly
two places that are not scanned by the asset pipeline: **ART.md Section 9** and
**`Docs/Sprites/README.md`**.

Remove the "pin head color" gap from Section 9 and record Verdant as shipped in the base asset.
Record the R6 provenance rule, the M3 platform-check obligation, and the hand-off constraints for
the future slicing/knockout plan. Then publish the two durable artifacts and commit them once.

**Durable artifacts published by this task (Q1/Q2) — everything else stays in the gitignored
`Docs/Sprites/work/` and is never committed:**

1. `Docs/Sprites/pin-head-mask.png` — the final post-Task-1.3 mask, minimal 1-bit or 8-bit
   greyscale, no ancillary chunks. Note that `*.png lfs` is repo-wide, so this becomes one LFS
   object; that is accepted deliberately, once.
2. `Docs/Sprites/README.md` — what the mask is, the top-left origin convention plus the Unity
   `rect.y = 768 - y_image_bottom` conversion (M4), the R6 provenance rule, and **both LFS OIDs**:
   the pre-recolor OID from Task 0.2 and the post-promotion OID from Task 1.4, with a statement
   that the mask is valid only against the post-promotion OID so a later sheet edit visibly
   invalidates it.
3. The plain-text records (checksums, chunk dumps, per-frame histograms) copied from
   `Docs/Sprites/work/` into `Docs/Sprites/`, as text — they are diffable and cheap.

**Line endings (m3):** `ART.md` is LF-only with no BOM; preserve that, or the "diff confined to
Section 9" criterion fails spuriously. Write `Docs/Sprites/README.md` LF-only for the same reason.

**Acceptance criteria:**
- [ ] ART.md Section 9 bullet 1 replaced with a "closed" record naming the date and the F-IDs affected (19 of 24 frames: F1–F13, F15–F18, F20–F21)
- [ ] Section 9 states that `Assets/Sprites/Reference/*` retains the original amber deliberately, as provenance, and must not be recolored or used to regenerate the working sheet (R6)
- [ ] **Nothing is written, added or modified under `Assets/` by this task** — `git status --porcelain -- Assets/` is empty (C3)
- [ ] Section 9 records that the slicing/knockout pass must re-run this plan's Task 2.1 diff afterwards
- [ ] Section 9 records the two slicing blockers found here — non-uniform frame layout with overlapping F17/F18 bounding boxes, and fully-opaque alpha requiring a knockout pass
- [ ] Section 9 records the **M3 platform obligation**: the first scene that renders this sprite must include a **Windows Standalone and a Linux Standalone** visual check of the pin head against the resolved compressed format, because no scene renders it today
- [ ] **Backstop for M1:** a grep of the whole of ART.md returns zero surviving stale frame-index citations (`frame 29`, `29–34`, `29-34`, `17–22`, `17-22`); any that Task 0.1 missed are re-pointed here to F-IDs, with no other wording changed
- [ ] `Docs/Sprites/pin-head-mask.png` exists, is 1408×768, is 1-bit or 8-bit greyscale, carries **no ancillary chunks**, and is the post-Task-1.3 mask
- [ ] `Docs/Sprites/README.md` exists and records: the mask's purpose, the top-left origin convention and the Unity y-conversion, the R6 provenance rule, and both the pre-recolor and post-promotion LFS OIDs of the sprite sheet
- [ ] `Docs/Sprites/work/` remains untracked and unstaged — `git status --porcelain` lists nothing from it (Q1)
- [ ] ART.md 2.3 hex values are unchanged; the Ember tier remains `#E8A33D` and is not confused with the removed amber
- [ ] No other ART.md section modified (diff confined to Section 9); ART.md remains LF-only with no BOM (m3)

```
Phase 3 dependency graph:
  2.1 ──┐
  2.2 ──┼──→ 1.4 ──→ 2.3 ──→ 3.1 [ART] close ART.md 9 + publish Docs/Sprites/
  (3.1 needs 2.3's PASS and 1.4's new LFS OID; it is strictly last and writes nothing in Assets/)
```

---

## 5. Full Task Summary

Listed in **execution order**, which after the Q4 restructure is not the same as numeric order.

| # | Tag | Task | Depends on | Parallel | Est. |
|---|---|---|---|---|---|
| 0.1 | `[ART]` | Correct ART.md frame map + origin convention | none | with 0.2 | 2h |
| 0.2 | `[QA]` | Workspace + `.gitignore` + pre-change baseline | none | with 0.1 | 2h |
| 1.1 | `[ART]` | Build/verify pin-head mask + cluster determination | 0.2 | no | 3h |
| 1.2 | `[ART]` | Apply Verdant mapping (working copy) | 1.1 | no | 3h |
| 1.3 | `[ART]` | Fringe/anti-alias cleanup (working copy) | 1.2 | no | 3h |
| 2.1 | `[QA]` | Pixel diff vs. baseline — **working copy** | 1.3 | with 2.2 | 2h |
| 2.2 | `[QA]` | Palette/legibility + contact sheet — **working copy** | 1.3 | with 2.1 | 2h |
| 1.4 | `[ART]` | Promote into `Assets/` in place (hash-bound) | 2.1, 2.2 + sign-off | no | 1h |
| 2.3 | `[QA]` | Unity import, compression and integrity | 1.4 | no | 1h |
| 3.1 | `[ART]` | Close ART.md 9 + publish `Docs/Sprites/` | 2.1, 2.2, 2.3, 1.4 | no | 1h |

**Critical path (m1 — arithmetic shown, since round 1 got this wrong):**

```
0.2 (2h) → 1.1 (3h) → 1.2 (3h) → 1.3 (3h) → [2.1 ‖ 2.2] (2h) → 1.4 (1h) → 2.3 (1h) → 3.1 (1h)
  2 + 3 + 3 + 3 + 2 + 1 + 1 + 1 = 16 hours
```

Sum of all task estimates: 2+2+3+3+3+2+2+1+1+1 = **20 hours**. Task 0.1 (2h) is off the critical
path — it runs parallel to 0.2 and is only needed before Task 3.1.

The Q4 restructure added **zero hours**: 2.1 and 2.2 moved earlier rather than being duplicated,
and the SHA-256 equality assertion in Task 1.4 is what removes the need to re-run them after
promotion. It is 1h longer than round 1's *corrected* 15h figure only because 2.3 no longer
overlaps 2.1/2.2 — it cannot, since it needs the file inside `Assets/`.

### 5.1 Verification ledger and sign-off record

Filled in during execution. Task 1.4 **must not run** until all four rows below are present and
name the same SHA-256. This section is the machine-checkable form of "verified bytes only".

| Record | Value | Filled by |
|---|---|---|
| Baseline sheet SHA-256 / LFS OID (pre-recolor) | _pending_ | Task 0.2 |
| Working-copy SHA-256 (post-fringe, candidate) | _pending_ | Task 1.3 |
| Task 2.1 result (PASS/FAIL) + SHA-256 tested | _pending_ | Task 2.1 |
| Task 2.2 result (PASS/FAIL) + SHA-256 tested | _pending_ | Task 2.2 |
| Contact-sheet sign-off (name + date) | _pending_ | human reviewer |
| Promoted file SHA-256 (must equal working copy) | _pending_ | Task 1.4 |
| New LFS OID (post-promotion) | _pending_ | Task 1.4 |
| Task 2.3 result (PASS/FAIL) | _pending_ | Task 2.3 |

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
6. **The ATTACK swing frame (F16) carries a smooth gradient swoosh on a character** (m5, found by
   the round 1 reviewer). F16 is the widest frame in the sheet at 245 px precisely because of this
   motion-trail arc, and it is rendered as a soft gradient. That conflicts with **ART.md Section
   3** ("no smooth painterly gradients on characters — that rendering language is reserved for
   backgrounds/atmosphere only") and with **ART.md Section 8**, which repeats the guard.
   **Explicitly out of scope for this plan**, and recorded here so it is not "fixed" ad hoc during
   the recolor: the swoosh is not pin-head-colored, it is not inside any Section 1.5 pin-head
   bounding box, and Task 2.1's "zero changed pixels outside the pin-head boxes" criterion means
   touching it would **fail** this plan's verification. Correcting it means re-authoring the
   effect as a hand-shaped ink form (ART.md 6) — a shape change needing its own plan, and best
   sequenced with item 5, since re-authoring ATTACK to 6 frames would redraw this frame anyway.

---

## 7. Resolved Decisions (round 1 open questions — now settled, not open)

The four questions this section raised in round 1 were answered authoritatively by the reviewer.
They are recorded here as decisions, with where each is implemented, so that nobody re-litigates
them mid-execution.

1. **Working-copy location — `Docs/Sprites/`, with a two-tier split. SETTLED.**
   `Docs/Sprites/work/` holds every iteration artifact and is **gitignored**; `Docs/Sprites/` root
   holds only the durable artifacts, committed once at close-out. The gitignore entry is
   mandatory, not conventional, because `*.png lfs` in `.gitattributes` is repo-wide (Section 2.3,
   M5). Neither directory may be assumed to exist — Git does not track empty directories (m2).
   *Implemented in:* Section 2.3, Task 0.2 (creation + `.gitignore`), Task 3.1 (publication).
2. **Retain the selection mask — yes, exactly one, committed once at close-out. SETTLED.**
   `Docs/Sprites/pin-head-mask.png`, minimal 1-bit or 8-bit greyscale, no ancillary chunks, with
   `Docs/Sprites/README.md` recording the origin convention and the source sheet's LFS OID so a
   later sheet edit visibly invalidates it. No intermediate mask is ever committed.
   *Implemented in:* Task 1.3 (export candidate), Task 3.1 (publish + README).
3. **Task 0.1 stays in this plan, narrowed. SETTLED.** The dependency is genuine — this plan's
   acceptance criteria cannot cite frame IDs that do not exist. Constraints: frame indices and
   counts only, no design or content claims; coverage extended to Section 9's stale indices (M1);
   the origin convention stated (M4); line endings preserved (m3).
   *Implemented in:* Task 0.1, with a grep backstop in Task 3.1.
   *M1/Q3 reconciliation:* Section 9's stale **indices** are re-pointed by Task 0.1, because it is
   the task that owns index correction and runs first, so no implementing agent ever reads a dead
   frame number. Task 3.1 — which edits Section 9 anyway — carries the verification criterion and
   fixes anything that survived. Section 9 bullet 2's *content* claim (the non-existent orange
   flash) is untouched by both and stays routed to Section 6 item 1.
4. **Promotion is gated behind verification. SETTLED.** 2.1 and 2.2 run against the working copy
   after 1.3; a recorded sign-off plus SHA-256 equality binds the promotion to the verified bytes;
   2.3 runs after 1.4. The round 1 defence ("a one-command LFS revert") was wrong in the way that
   matters: it holds for the working tree but not for LFS history, which CLAUDE.md step 4 pushes.
   *Implemented in:* Section 2.4, Phase 2 ordering, Section 5.1 ledger, Tasks 2.1/2.2/1.4/2.3.

**No open questions remain.** Anything that emerges during execution goes back through the
CLAUDE.md pipeline rather than being decided in-flight.

---

## 8. Review Notes — Round 1 (implementation-plan-reviewer, 2026-08-02)

**Status: CHANGES REQUESTED.** Research quality and regression discipline are strong;
frame count (24), the missing DIE orange flash, importer settings, LFS tracking, and
zero-reference blast radius were all independently verified by the reviewer and hold.
The items below are surgical, not a structural redraft.

**Two corrections applied by the routing agent (Claude) before recording this section**,
since they materially change what's blocking:

- **C2 (originally Critical, "GUID not under version control") is RESOLVED, not blocking.**
  The reviewer's session read a stale/local git snapshot. Verified directly:
  `git ls-files --error-unmatch Assets/Sprites/Quirrel_Sprites.png.meta` succeeds, and the
  file has been tracked and pushed since commit `3672612` ("Add art bible and Claude agent
  configs; track sprite meta files") — GUID `62b4e791ec637c245b6ea820090a2f94` is committed.
  Task 1.4 does not need to add a "commit the .meta" step; it already exists in history.
  Downgraded to a note: Task 1.4 should still confirm the `.meta` is unchanged post-edit
  (already an existing acceptance criterion) rather than treat committing it as new work.
- **C1 (inter-frame head-colour drift) downgraded from Critical to Major, pending Task 0.2's
  own measurement.** A direct visual read of `Assets/Sprites/Quirrel_Sprites.png` shows the
  same two-tone split (gold lit half / red-orange shadow-rim half) consistently across IDLE,
  WALK, JUMP, and ATTACK. The DEFEND pose shows the disc face-on rather than edge-on, which
  changes the *visible proportion* of gold vs. orange but does not obviously indicate the
  underlying hues shifted between states — this looks like a viewing-angle artifact rather
  than true inter-frame drift. That said, this is an eyeball read, not a pixel measurement,
  so the reviewer's fix stands as a cheap, worthwhile check: Task 0.2 still adds a per-frame
  hue/S/V histogram and modal value for each of the 19 heads, and Task 1.1 still makes an
  explicit intra-head-vs-inter-frame determination before Task 1.2 commits to a transform.
  It is no longer a plan-blocking concern on its own — Task 1.2's mapping should default to
  the continuous hue-preserving approach unless Task 0.2's data says otherwise.

**Everything below is the reviewer's original finding, unedited.**

### Blocking (must fix before ACCEPTED)

- **C3 — Task 3.1 must not write into `Assets/`.** The provenance note contradicts Section 2.1
  ("zero new assets in `Assets/`") and Section 2.3. Move it to ART.md Section 9 and
  `Docs/Sprites/README.md`; remove `Assets/` from Task 3.1's `Touches:` line.

### Major

- **M1** Task 3.1 must also re-point ART.md **Section 9**'s stale frame citations
  ("frame 29", "DIE frames 29-34") to F-IDs. Re-pointing indices is in scope; withdrawing
  bullet 2's content claim stays routed to Section 6 item 1.
- **M2** Promotion currently precedes all verification, and CLAUDE.md pushes — see Q4 below.
- **M3** No compression/platform check. Record the resolved compressed texture format in
  Task 0.2; add to Task 2.3 that the Standalone-tab format is unchanged and the compressed
  preview shows no new banding on the head at 4x. Record in ART.md Section 9 that the first
  scene rendering this sprite must include a Windows and a Linux Standalone visual check.
- **M4** Task 0.1's frame table must state the coordinate origin (top-left, image space) and
  the conversion to Unity's bottom-left sprite rects (`y_unity = 768 - y_bottom`).
- **M5** `*.png lfs` in `.gitattributes` is repo-wide. See Q1/Q2 for the required policy.
- **M6** Replace unmeasurable criteria: delete Task 2.2's "does not read as AI glow";
  quantify "no visible drift" as max dH <= 5deg and dV <= 0.05 between per-frame modal head
  colours; quantify background legibility as dV >= 0.20 against `#2B333E`-`#3B4652` and
  `#163542`-`#1E4A56`; restate Task 1.2's "+/- 3 per channel" against cluster modal values
  with a stated percentage for non-boundary pixels.
- **M7** Task 1.4 must state that the Unity Editor is closed during the in-place overwrite and
  opened exactly once afterwards, in Task 2.3.

### Minor

- **m1** Critical path is 15h, not 14h (2+3+3+3+1+2+1).
- **m2** `Docs/Sprites/` cannot be assumed to exist (git does not track empty dirs) — create it
  in Task 0.2.
- **m3** `*.md` has no `text` attribute in `.gitattributes`; preserve existing line endings when
  editing ART.md or the "diff confined to Section X" criteria will fail spuriously.
- **m4** Task 0.1 must state that the F1-F24 table goes in Section 7.
- **m5** Add to Section 6: the ATTACK swing frame (F16) contains a smooth gradient swoosh on a
  character, conflicting with ART.md 3 and 8. Out of scope; record so it is not fixed ad hoc.

### Answers to Section 7 open questions — authoritative, implement as stated

1. **Working-copy location — `Docs/Sprites/`, approved, with a split.**
   `Docs/Sprites/work/` holds all iteration artifacts and **must be added to `.gitignore` in
   Task 0.2**; convention alone is insufficient because `*.png lfs` is repo-wide and a stray
   `git add -A` creates permanent LFS objects. `Docs/Sprites/` root holds only the durable
   artifacts committed at close-out. Text records (checksums, chunk dumps, histograms) are
   plain text and committed freely. Create the directory; do not assume it exists.
2. **Retain the mask — yes, exactly one, committed once at close-out.**
   `Docs/Sprites/pin-head-mask.png` (final, post-Task-1.3), written as a minimal 1-bit or 8-bit
   greyscale PNG with no ancillary chunks, plus `Docs/Sprites/README.md` recording what it is,
   the top-left origin convention, and the **LFS OID of the source sheet it was derived from**
   so a later sheet edit visibly invalidates it. No intermediate masks are ever committed.
3. **Task 0.1 stays in this plan, narrowed.** The dependency is genuine — acceptance criteria
   cannot cite frame IDs that do not exist — and a separate pipeline pass is disproportionate.
   Constraints: frame indices and counts only, no design/content claims; extend coverage to
   Section 9's stale indices (M1); state the origin convention (M4); preserve line endings (m3).
4. **Yes, gate promotion behind verification. Restructure as follows (zero added hours):**
   - Task 2.1 `Depends on:` -> **1.3**; runs against the working copy.
   - Task 2.2 `Depends on:` -> **1.3**; runs against the working copy. New deliverable: a
     baseline-vs-recoloured **contact sheet** for all 19 pin-bearing frames at 1x and 4x,
     written to `Docs/Sprites/work/`. This is the approval artifact.
   - Task 1.4 `Depends on:` -> **2.1, 2.2**. New criteria: recorded sign-off on the Task 2.2
     contact sheet exists in this plan file before the write; and the SHA-256 of the promoted
     file equals the SHA-256 of the working copy that passed 2.1/2.2 (this equality makes
     re-running 2.1/2.2 after promotion unnecessary).
   - Task 2.3 `Depends on:` -> **1.4**, plus the M3 and M7 criteria.
   - Update the Phase 1 and Phase 2 dependency graphs. Renumbering (1.4 -> Phase 3 "Promote")
     is optional and cosmetic.
   Rationale: the plan's defence ("a one-command LFS revert") holds for the working tree but
   not for LFS history, and CLAUDE.md step 4 commits and pushes. Verified bytes only.

Re-submit for round 2 with C3 and M1-M7 applied (C1/C2 handled per the corrections above).
No structural redraft required.

---

### Round 1 items addressed — revision log (architect, 2026-08-02)

Section 8 above is left intact as the historical review record. This table records only *where*
each finding was applied; it does not restate or amend the findings.

| Item | Status | Where applied |
|---|---|---|
| C1 (inter-frame drift) | Applied as Major | Task 0.2 (per-frame, per-cluster modal H/S/V table + spread figure); Task 1.1 (written intra-head-vs-inter-frame determination, ≤ 8° threshold, default = continuous transform); Task 1.2 (must match that determination) |
| C2 (`.meta` versioning) | No action needed | Confirmed resolved by the routing agent; Task 1.4 now says so explicitly — the `.meta` is tracked since `3672612`, so the criterion is "prove it did not change", not "commit it" |
| **C3** (no writes to `Assets/`) | **Fixed** | Task 3.1 `Touches:` no longer includes `Assets/`; provenance note relocated to ART.md Section 9 + `Docs/Sprites/README.md`; new criterion `git status --porcelain -- Assets/` is empty |
| **M1** (Section 9 stale indices) | **Fixed** | Task 0.1 re-points "frame 29" → F22 and "DIE frames 29–34" → F22–F24 with wording otherwise unchanged; Task 3.1 carries a grep backstop. Reconciliation of M1 vs Q3 written up in Section 7 item 3 |
| M2 (promotion before verification) | Fixed via Q4 | Section 2.4 + Phase 2 restructure |
| **M3** (compression / platform) | **Fixed** | Section 1.3 (authored platform settings table), new risk R7 (Section 4.7), Task 0.2 (record resolved format + memory size), Task 2.3 (format unchanged, 4× banding check on F16/F20/F21), Task 3.1 (ART.md records the Windows + Linux Standalone obligation for the first scene that renders the sprite) |
| **M4** (coordinate origin) | **Fixed** | Section 1.5 preamble (convention + conversion formulas + worked example); Task 0.1 criterion; repeated in `Docs/Sprites/README.md` via Task 3.1 |
| M5 (repo-wide `*.png lfs`) | Fixed via Q1 | Section 2.3 (why gitignore, not convention); Task 0.2 (`.gitignore` entry + `git check-ignore` criteria); the committed mask is acknowledged as one deliberate LFS object |
| **M6** (unmeasurable criteria) | **Fixed** | Task 2.2: "AI glow" criterion deleted; drift quantified as ΔH ≤ 5° / ΔV ≤ 0.05 across per-frame modals; legibility quantified as ΔV ≥ 0.20 worst-case against both ART.md 2.1 ranges. Task 1.2: "± 3 per channel" restated against cluster **modal** values with a defined non-boundary population and a ≥ 95% threshold |
| **M7** (Editor closed) | **Fixed** | Task 1.4 (Editor closed for the duration, not reopened by that task, with rationale); Task 2.3 (opens it exactly once, and owns observing the import) |
| m1 (critical path) | Fixed | Section 5: 16h under the new ordering, arithmetic shown, plus why it differs from the corrected 15h |
| m2 (`Docs/Sprites/` existence) | Fixed | Section 2.3 and Task 0.2 create both directories; no existence assumed |
| m3 (line endings) | Fixed | Tasks 0.1 and 3.1: ART.md verified LF-only, no BOM; preserve, with a `git diff --stat` criterion |
| m4 (F-table location) | Fixed | Task 0.1: the F1–F24 table goes **in Section 7**, after the timing table, with the reason |
| m5 (F16 gradient swoosh) | Recorded | Section 6 item 6, with an explicit note that touching it would fail Task 2.1 |
| Q1–Q4 | Implemented as stated | Section 7 now records them as settled decisions with pointers to where each lands |

---

## 9. Review Notes — Round 2 (implementation-plan-reviewer, 2026-08-02)

**Status: CHANGES REQUESTED.** All round-1 items (C3, M1–M7, m1–m5, Q1–Q4) were verified as
actually applied in the task text, criteria and graphs — not merely logged. The Q4 restructure is
consistent across the task `Depends on:` lines, both Phase 2 and Phase 3 graphs, Section 2.4 and
the Section 5 table. Critical-path arithmetic (16h) and the 20h total both check out. Every
importer, `.gitattributes` and ART.md fact cited in Sections 1.3–1.6 was independently re-verified
against the repo and holds, including the C2 resolution (`.git/index` tracks the `.meta`;
`.git/logs/HEAD` confirms commit `3672612`).

The items below are all new — introduced or exposed by the round-2 edits. Two of them cause the
plan to fail its own promotion gate as written.

### Blocking (must fix before APPROVED)

- **C1 — The Task 2.1 gate contradicts the Task 1.1 mask. As written, promotion cannot pass.**
  Task 1.1 requires "the 17 px fragment at (534, 672) is included" in the mask. F21's pin-head
  regions in Section 1.5 are x551–574 and x604–627; x=534 falls inside **neither**, and F21's
  stated area (1,167 px = 523 + 644) excludes it. Task 2.1 then asserts "Every changed pixel falls
  inside a Section 1.5 pin-head bounding box; zero changed pixels elsewhere". Recoloring the
  fragment fails 2.1; not recoloring it fails 1.1 and leaves the amber speck R4 exists to prevent.
  The same conflict applies to the 85 sub-20 px fringe fragments (135 px) that Section 1.5 places
  "at head/shaft/ink-line boundaries" and that Task 1.3 is required to resolve — the plan never
  says those lie inside the listed boxes, and at head/shaft boundaries some will not.
  **Fix (both halves required):**
  1. In Section 1.5, add the fragment to the F21 row as an explicit third region with its bbox and
     area, correct F21's total, and add one line defining the fringe allowance: *"Task 1.3 may
     additionally alter anti-alias pixels within 1 px of a listed region boundary; these are
     enumerated in the Task 1.1 mask and are the only pixels permitted outside the boxes."*
  2. Replace the Task 2.1 criterion with:
     `- [ ] Every changed pixel falls inside the Task 1.1 mask; the mask's per-frame extent equals the Section 1.5 regions plus the enumerated out-of-box fragments and the 1 px fringe allowance; zero changed pixels anywhere else`

- **C2 — Task 1.4's new-LFS-OID criterion cannot be satisfied, and the method named is wrong.**
  The criterion is "`git lfs ls-files -l` lists it with a new OID". `git lfs ls-files` reads the
  **index/HEAD**, not the working tree: for a modified-but-unstaged file it reports the
  *pre-recolor* OID. Obtaining a new OID from it requires `git add`, which contradicts Task 0.2's
  "Nothing produced by Phase 0, 1 or 2 is ever staged" and the plan's own "verified bytes only
  reach LFS history" argument (Section 2.4) — staging writes the object into `.git/lfs/objects`
  before Task 2.3 has run. Task 3.1's README criterion then depends on that unobtainable value.
  **Fix:** Git LFS OIDs *are* the SHA-256 of the file contents, so the value already exists.
  Replace the criterion with:
  `- [ ] The file is still LFS-tracked (`git check-attr filter -- Assets/Sprites/Quirrel_Sprites.png` reports `filter: lfs`); the post-promotion LFS OID is recorded in Section 5.1 as `sha256:<promoted-file SHA-256>` — identical to the hash asserted above, requiring no staging. `git lfs ls-files -l` is **not** the source for this value: it reads the index and will report the pre-recolor OID until the file is staged`
  Task 0.2's use of `git lfs ls-files -l` for the *pre-change* OID is correct and stays.

### Major

- **M1 — Nothing in the plan commits the result, and the one statement about committing excludes
  it.** Task 0.2 states "only Task 3.1 commits, and only from `Docs/Sprites/` root", and Task 3.1's
  criteria commit only the mask, README and text records. `Assets/Sprites/Quirrel_Sprites.png`,
  `ART.md` and `.gitignore` are therefore never committed by any task — while CLAUDE.md step 4
  requires the work to land in Git. Worse, the *timing* of that commit is the plan's central safety
  property (an LFS push is permanent) and it is unstated, so a commit between Task 1.4 and Task 2.3
  would push bytes that have not passed the R1/R7 gates.
  **Fix:** reword Task 0.2's sentence to "Nothing is staged before Task 3.1", and add to Task 3.1:
  `- [ ] No commit containing `Assets/Sprites/Quirrel_Sprites.png` is made until Section 5.1 records Task 2.3 PASS; this task then makes the plan's single commit, containing exactly: the recolored PNG, `ART.md`, `.gitignore`, and the `Docs/Sprites/` durable artifacts — and nothing from `Docs/Sprites/work/``

- **M2 — Task 2.2's "head-vs-shaft value separation ≥ 0.15 in V" is unsatisfiable under the
  plan's own numbers, and R5's supporting claim is arithmetically wrong.** The criterion does not
  say which pairing is measured. The shaft carries both `#C9CCD1` (V 0.82) and `#9AA0AA`
  (V 0.667); the head's lit modal is V 0.635. Worst-case pairing is 0.667 − 0.635 = **0.032**,
  well under 0.15 — and the gold it replaces was 0.886 vs 0.82 = 0.066, so worst-case separation
  gets *worse*, not better as Section 4.5 asserts. Unlike the background criterion, this one has no
  "nearest value / worst case" clause, so a literal measurement blocks promotion for something
  ART.md 4.2 does not actually forbid (it requires distinct value **and** color; hue carries the
  read here — grey vs green).
  **Fix:** state the pairing and add the hue leg:
  `- [ ] Head-vs-shaft separation on F18 and F21 satisfies ART.md 4.2 by **both** legs: ΔV ≥ 0.15 between the head lit modal and the shaft **highlight** family (`#C9CCD1`), **and** ΔH ≥ 60° between the head modal and the nearest shaft value; where ΔV against the shaft *shadow* family (`#9AA0AA`) is < 0.15 the measured figure is recorded and the greyscale silhouette pass below is the deciding check`
  Also correct Section 4.5's "this should improve" to state the two pairings separately.

- **M3 — Section 1.5's per-frame areas do not reconcile with the headline figure, and two
  acceptance criteria depend on that reconciliation.** The 19 frame areas sum to **7,844 px**;
  adding the 135 px of fringe fragments, the 17 px fragment and the 2 text pixels gives 7,998 —
  leaving **461 px (5.4%) of the stated 8,459 unaccounted for**. Task 2.1 reconciles changed-pixel
  counts against these numbers with **no tolerance clause** (Task 1.1 at least says "within a
  stated tolerance"), so the gate trips on a bookkeeping gap rather than a real defect.
  **Fix:** either state where the remaining ~461 px live (most likely the inclusive-threshold
  boundary ring already described in Section 2.2), or add to Task 2.1:
  `- [ ] Per-frame changed-pixel counts reconcile with Section 1.5 within ±10% per frame and ±2% sheet-wide against the Task 0.2 re-measurement, which supersedes Section 1.5 where they differ; any per-frame deviation beyond tolerance is explained in Section 5.1, not waived`

- **M4 — Task 3.1's dependency on Task 0.1 is missing, and three places disagree about what 0.1
  gates.** Task 3.1 carries the M1 grep backstop ("any that Task 0.1 missed are re-pointed here"),
  which presumes 0.1 has run, and both tasks write `ART.md` — yet 3.1's `Depends on:` lists only
  2.1, 2.2, 2.3, 1.4, and the Section 5 table agrees. Meanwhile the Phase 0 graph draws 0.1 as
  gating **Phase 1**, which Task 1.1 (`Depends on: 0.2`) and Section 5 both contradict, and the
  Section 5 prose says 0.1 "is only needed before Task 3.1" — an ordering no `Depends on:` line
  expresses. Two ART.md writers with no declared ordering is exactly the coupling this plan is
  otherwise rigorous about.
  **Fix:** Task 3.1 `Depends on:` → `Tasks 0.1, 2.1, 2.2, 2.3 (all must pass) and Task 1.4`;
  same in the Section 5 row; and redraw the Phase 0 graph so only 0.2 feeds Phase 1, with 0.1
  feeding 3.1:
  ```
  0.1 [ART]  (ART.md) ───────────────────────────────→ 3.1
  0.2 [QA]   (.gitignore + workspace + baseline) ────→ Phase 1
  ```

### Minor

- **m1** Phase 2's preamble says 2.1 and 2.2 may run concurrently "because neither writes". Both
  write Section 5.1 of this file, and 2.2 writes the contact sheet — as its own `Touches:` line and
  Section 4.8 both state. Reword to "because neither writes the artifact under test; their only
  shared write is distinct rows of the Section 5.1 ledger".
- **m2** The revision log says Task 2.3's 4× banding check covers "F16/F20/F21". The task says
  **F15**/F20/F21, and F15 (160 px) is the correct choice per Section 1.5. Fix the log.
- **m3** The grep backstops in Tasks 0.1 and 3.1 list `29`, `29–34`, `29-34`, `17–22`, `17-22` but
  omit **`23–26` / `23-26`** — ART.md line 190 (Section 4.2) reads "`DEFEND` frames 23–26",
  verified live. The substantive criterion covers DEFEND, but the backstop that exists to catch a
  miss would not catch this one. Add both tokens to both lists.
- **m4** Task 1.2's "No output pixel in the head exceeds S 0.55 or V 0.70" does not name a
  population, two criteria after the task carefully defines one. Boundary pixels blending toward
  the shaft highlight (`#C9CCD1`, V 0.82) can legitimately exceed V 0.70. Scope it to the
  non-boundary population.
- **m5** Task 2.2 requires each of the 19 per-frame modals to sit "within the Task 1.2 anchor
  tolerance" (± 3 per channel) while the criterion above it allows an inter-frame ΔV of 0.05
  (≈ ± 13/255). The stricter number is also noisy on F15's 160 px head. State one tolerance, and
  make the "per-frame modal head color" wording per-cluster, consistent with the criterion below it.

### Not blocking, for the architect's judgment

Task 0.2 at 2h now carries directory creation, the `.gitignore` edit, 24 crops, whole-file and
per-frame checksums, a chunk dump, per-cluster H/S/V histograms and modals for 19 frames, and two
Inspector reads. It is still within band if scripted, but it is the one estimate in the plan with
no slack; splitting the per-cluster characterization into its own 1h `[QA]` task would be
defensible if it starts to run long.

**Re-submit for round 3 with C1, C2 and M1–M4 applied.** No structural redraft required — C1, C2
and M1 are the only ones that change execution; the rest are wording and dependency-line fixes.
