ART.md — Hollow Knight: Quirrel Art Bible
=========================================

This is the single source of truth for visual direction on this project. Every new
asset, shader, UI element, and VFX should be checked against this document before
it ships. If something here needs to change, propose the change and align on it
explicitly — don't silently drift.

Status: living document, v1. Grounded in the concept art in `Docs/UserRequirements/`
and the first generated sprite pass in `Assets/Sprites/`. See **Section 9, Asset
Alignment Notes** for known gaps between what's already in the repo and what this
bible specifies.

---

## 1. Art Pillars & Tone

Hand-drawn, gothic-whimsical bug kingdom. Melancholic but charming — grief and
decay rendered with warmth, not nihilism. This is **Hollow Knight / Silksong's**
specific visual language, not generic "dark fantasy":

- **Ink-line silhouettes first.** Every character, enemy, and hazard is designed
  as a black shape before it's a colored one. If the shape doesn't read at a
  glance, the color pass won't save it.
- **Small, fragile figures in vast, ornate architecture.** The camera and
  environment scale make Quirrel look delicate against cathedral-huge ruins and
  cave ceilings — vulnerability is a visual theme, not just a stat.
- **Quiet melancholy over horror.** Decay and infection are sad, not gross.
  Restraint in gore, emphasis on stillness, fog, and lonely lighting (see
  `Quirrel_resting_on_bench.png`, `Quirrel_observing_egg.png`).
- **Whimsy in the character design, weight in the world.** Quirrel himself is
  round, soft-edged, almost toy-like — a deliberate contrast against sharp,
  looming, detailed backgrounds. That contrast *is* the house style.
- **High contrast, low chroma.** The world runs on desaturated blue-grey and
  charcoal value contrast; color is spent sparingly and always means something
  (danger, warmth, spirit, currency, infection).

---

## 2. Color Palette

Baseline rule: **the world is desaturated blue-grey/charcoal.** Saturated color
is a signal, not decoration. If a background needs "more interest," add ink
detail or fog density — not chroma.

### 2.1 Depth layers (environment base tones)

| Layer | Purpose | Hex | Notes |
|---|---|---|---|
| Void / far background | Deepest backdrop, sky, cave ceiling | `#0A0E14` | Near-flat, heavy blur |
| Far background | Distant architecture / cave walls | `#10151F` – `#1A2230` | Low detail, high haze |
| Midground wash (cool/cave biomes) | Teal-leaning areas (fungal caves, water) | `#163542` – `#1E4A56` | See `Quirrel_environment.png` |
| Midground wash (indigo biomes) | Gothic architecture, ruins, night | `#16213A` – `#274069` | See `Quirrel_resting_on_bench.png` |
| Foreground silhouette crush | Near-camera geometry, foliage, pillars | `#0B0C10` (near-black, slight blue bias) | Always crushes toward black regardless of biome tint |
| Gameplay midground floor/set-dressing | Where player actually stands | `#2B333E` – `#3B4652` | Must stay legible against player/enemy silhouettes |

### 2.2 Character base tones (Quirrel)

| Element | Hex | Notes |
|---|---|---|
| Mask, healthy | `#F2EEE3` | Warm cream-white, not pure white — matches `Quirrel.png` |
| Mask ink line | `#1B1B1F` | All linework, not pure black |
| Thorax / body (light) | `#7C8699` | Grey-blue, matches reference |
| Thorax / body (shadow) | `#5B6478` | One-step-down cel shadow |
| Cloak / wing-case | `#2E323D` | Charcoal-navy |
| Cloak shadow accent | `#21242C` | |
| Legs | `#17181D` | Near-black ink, thin, no midtone |

### 2.3 Pin weapon (default / "Verdant" pin)

| Element | Hex | Notes |
|---|---|---|
| Shaft (metal highlight) | `#C9CCD1` | |
| Shaft (metal shadow) | `#9AA0AA` | |
| Head, default green | `#6FA25C` | The canonical starting pin — matches a drawing-pin's plastic head |
| Head shadow (green) | `#4A7A3E` | |

**Pin head color is a gameplay-legible upgrade signal.** Reserve distinct,
consistent hues per pin tier so players read power/type at a glance:

| Pin tier (example) | Head hex | Read as |
|---|---|---|
| Verdant (default/starting) | `#6FA25C` | Balanced, parry-leaning |
| Ember | `#E8A33D` | Power / aggression variant |
| Crimson | `#C94F4F` | High-risk aggressive variant |
| Bone-white | `#E9EDF2` | Late-game "true form" variant |

Exact unlock order and stat meaning belong to @gameplay-programmer; this table
only pins down the *color* so future upgrade art stays consistent.

### 2.4 UI / accent colors

| Purpose | Hex | Notes |
|---|---|---|
| Soul / Focus resource | `#BFE3FF` (fill) / `#7FB8E0` (shadow) | Pale spiritual blue-white, genre-standard; matches bench-lamp glow in reference art |
| Currency (Geo-equivalent) | `#F4C97A` | Warm gold, ties to spore-mote color in environment art |
| Save bench / safe-point glow | `#F6E7C7` | Warmer and softer than Soul-blue — reads as "warmth/safety," not "resource" |
| Neutral UI / prompt icons | `#EDEAE0` line on transparent | No color unless the prompt is actionable right now |
| Interactable highlight (generic) | `#F4C97A` at low opacity pulse | Reuses currency-gold family — "this can be used" |
| Mask pip, filled (HUD) | Fill `#F2EEE3` / ink `#1B1B1F` (Section 2.2 tokens, reused as-is) | The non-diegetic upper-left Mask-count HUD row (`MaskHUD`) — distinct from Section 2.5's on-character diegetic crack system |
| Mask pip, empty (HUD) | `#1B1B1F` outline on transparent fill | Same HUD row, depleted-pip state; interior knocked out to alpha 0, ink outline stays opaque |

### 2.5 Health / damage feedback — mask crack system

This is the primary HP feedback system. **Cracks are neutral ash/grey/black —
never infection-orange.** Keeping this palette distinct from Section 2.6 is a
hard rule: if mask damage ever reads as "infected," the two systems collapse
into one signal and both become confusing.

| Stage | Trigger | Visual | Hex |
|---|---|---|---|
| 0 — Whole | Full health | Clean cream mask, no linework beyond the base ink line | `#F2EEE3` |
| 1 — Hairline | First point of damage | One fine crack line, no missing geometry | Crack line `#B9B4AA` on base cream |
| 2 — Fractured | Mid health loss | 2–3 widening cracks, one small chip missing at the rim | Chip edge `#8C8781`, exposed inner edge `#6B675F` |
| 3 — Critical | Last point before death | Large sections missing, mask barely holds shape, faint dust/ash motes drifting off it | Exposed charcoal `#4A4640`, dust particle `#C9C4B8` at low opacity |
| 4 — Shatter | Health reaches 0 | Mask breaks into falling shards, character dies | Matches existing DIE frames (see `Assets/Sprites/Quirrel_Sprites.png`, frames 29–34) |

Frame count for mask health is intentionally decoupled from max-HP count —
@gameplay-programmer maps HP thresholds to these 5 visual stages regardless of
how many total HP points the player has (tutorial max HP is 4; later upgrades
raise the ceiling without needing new mask art per point).

### 2.6 Infection theme

| Purpose | Hex | Notes |
|---|---|---|
| Infection core / glow | `#FF7A3D` | The *only* place saturated orange appears in the game |
| Infection crust / ooze | `#C94A1E` | Darker, drier variant for crusted growths |
| Infection fissure glow (thin lines) | `#FFB27A` at high emissive/bloom | Use sparingly, on cracks/fissures only |

This orange is reserved exclusively for the infection and must never be reused
for hit-sparks, mask damage, or generic "ouch" flashes (see Section 9 for a
known conflict in the current placeholder sprites).

---

## 3. Line & Rendering Style

- **Ink outlines on every character and interactive object.** Line weight
  roughly 2–3px at native sprite resolution (see Section 7 for PPU). Line color
  is never pure black (`#000000`) — use `#1B1B1F` or a hue-shifted near-black
  per palette family, matching the reference art.
- **Flat cel shading, 2–3 tone steps.** Base tone, one shadow step, occasionally
  a rim-light step. No smooth gradients on characters — gradients are reserved
  for backgrounds and atmosphere (fog, glow, bloom).
  See `Assets/Sprites/Quirrel_Sprites.png` for the shading step count already
  established.
- **Painterly, soft backgrounds vs. crisp foreground line.** Backgrounds use
  heavy blur/soft airbrush rendering and bloom-like glow (see
  `Quirrel_environment.png`, `Quirrel_observing_egg.png`) — this is the
  "atmospheric perspective" mechanism, not just a color shift. Foreground
  characters and interactive geometry stay crisp and ink-lined by contrast.
- **Silhouette readability rule (hard requirement):** the player, every enemy,
  and every hazard must be identifiable as a pure-black silhouette at gameplay
  distance. Test by dropping a screenshot to black-and-white silhouette —
  if two things become indistinguishable, the shapes need revision, not the
  color.
- **Contrast hierarchy**, front to back: interactive foreground objects (pin,
  benches, doors) > Quirrel > enemies/hazards > traversable midground geometry
  > decorative background. Never let background detail compete with player
  silhouette value.

---

## 4. Character Design Language

### 4.1 Quirrel — mask

The mask is a paper/cloth conical hat-mask, his old teacher's, worn as his
"head." It is the primary health-feedback surface (see Section 2.5) — this is
diegetic UI, not a floating HP bar, and should be treated with the same care as
a UI element: always legible against any background.

- Crack stages read left-to-right as *shape* changes (missing geometry, not
  just added decal lines) so they're visible even in silhouette at stage 3–4.
- The shatter (stage 4 / death) is the existing DIE animation in
  `Assets/Sprites/Quirrel_Sprites.png` (frames 29–34) — mask separates from
  the body and falls away. This is already on-model; do not restyle it without
  updating this doc first.
- Never tint the mask itself with infection orange, even during the death
  sequence — his death is exhaustion/damage, not corruption. Keep it ash/grey.

### 4.2 Quirrel — pin weapon (point vs. head)

The pin reads as two functionally distinct silhouette zones:

- **Point (attack):** thin, long, steel-grey shaft, near-white highlight edge.
  Reads as a line/vector — during attack swings it should extend Quirrel's
  effective silhouette significantly (see `ATTACK` frames 17–22 in the existing
  sheet), selling reach.
- **Head (block/parry):** the flat colored disc, held forward during block
  (see `DEFEND` frames 23–26). This is the color-coded upgrade slot (Section
  2.3). During a successful parry, consider a brief bright flash/outline pulse
  on the head only — not the whole character — so parry timing reads precisely
  even in motion.
- Keep point and head visually distinct at all times (different value AND
  color, never just a recolor of the same shape) so players can tell attack-
  ready vs. block-ready poses apart at a glance, including from enemy POV
  telegraphs.

### 4.3 Enemy & infection visual tells

Two enemy families, visually opposed on purpose:

**Healthy insect (uninfected):**
- Clean ink linework, symmetric bodies, calm/idle motion with gentle easing.
- Muted natural palette pulled from the same desaturated blue-grey base, with
  small amounts of natural bug coloring (moss green, dull red-brown, tan,
  slate) — never the infection orange family.
- Tutorial's small flying stinger enemy is this family: simple, slow,
  telegraphed wind-up, unthreatening silhouette so it reads as "a first lesson"
  rather than "a threat."

**Infected:**
- Cracked/broken exoskeleton with glowing orange fissures (`#FF7A3D` family,
  Section 2.6) — the *only* place this orange appears on a character.
- Weeping ooze/crust at cracks (`#C94A1E`), asymmetric bulging or growth
  breaking the original creature's silhouette symmetry.
- Motion language shifts too, not just palette: erratic, jittery, or
  too-slow-then-sudden movement, breaking the calm easing curves used for
  healthy creatures. Infection should be legible even with color vision
  differences or in a silhouette pass, via broken symmetry and jitter alone.

---

## 5. Environment & Parallax

Fixed named layers, fixed scroll ratios — do not improvise per-scene. This
keeps draw calls predictable and depth consistent across the whole map.

| Layer | Scroll ratio (approx.) | Detail / treatment |
|---|---|---|
| Void backdrop | 0.05x | Near-flat gradient or color, heaviest blur, sets biome hue |
| Far background | 0.2x | Distant architecture/cave silhouette, heavy atmospheric haze, low detail |
| Midground wash | 0.4x–0.5x | Where "the quiet scenery" lives — this is the desaturated, soft-painted layer that should never compete with gameplay elements |
| Near-midground set-dressing | 0.7x | Closer scenery, slightly more saturated/detailed than midground wash |
| Gameplay layer | 1.0x | Player, enemies, interactables, collision geometry — full contrast, crisp ink line |
| Near-foreground overlay | 1.2x–1.5x | Crushed near-black silhouettes passing in front of camera (foliage, pillars, mushroom caps) for depth — matches the mushroom-silhouette foreground in `Quirrel_environment.png` |
| Particle/FX layer | Independent, slightly slower than camera | Bioluminescent motes/spores, additive blend, warm gold (`#F4C97A` family) in fungal/cave biomes or cool white-blue (`#BFE3FF` family) in architectural/night biomes |

Depth is sold primarily through **atmospheric perspective** — desaturation and
haze density increasing with distance — with parallax speed as a secondary,
supporting cue. A background that's just "slower" without also going flatter
and greyer will look like cardboard cutouts, not depth.

Let backgrounds go quiet (lower detail density, less motion) specifically in
combat arenas so the player silhouette never gets lost mid-fight.

---

## 6. UI / VFX Accent Language

- **Soul/Focus, currency, and save-benches each get one dedicated color**
  (Section 2.4) and never borrow each other's hue. A player should be able to
  tell "this glow is currency" vs. "this glow is a safe bench" from color
  alone, at a glance, without reading text.
- **VFX are hand-authored shapes, not generic asset-store bursts.** Hit sparks:
  small, sharp, angular ink-line shapes (think ink-splatter, not circular
  particle bursts), colored pale/near-white by default — reserve saturated
  color in an impact effect for a specific meaning (e.g., a parry-success flash
  in the pin-head's current tier color).
- **Dash/dodge trails** (if/when added): thin, fading silhouette echoes of
  Quirrel's own shape in his cloak tone, not a colored streak.
- **No drop shadows on UI.** Depth in UI is conveyed by value and ink line,
  matching the game world's own rendering language, not by drop-shadow/bevel
  skeuomorphism.
- **No unmotivated bloom.** Glow is only used where the fiction supports an
  actual light source (lamps, soul, infection, spore bioluminescence) — never
  as a generic "make it look nice" post effect over flat art.

---

## 7. Practical Notes — 2D Unity Pipeline

Confirmed project facts (do not assume beyond what's verified here):

- **Unity 2021.3.45f2**, Built-in Render Pipeline. No URP package is currently
  installed (`Packages/manifest.json` has no `com.unity.render-pipelines.*`
  entry) — **2D Lights / Light2D and Shader Graph are not available yet.** Any
  "lighting" described in this doc (bench glow, spore bioluminescence, infection
  fissure glow) must be faked today via sprite gradients, additive-blended
  sprites/particles, and vertex-color darkening — not `Light2D` components. If
  the project adopts URP's 2D Renderer later, treat that as a full pipeline
  migration worth its own ADR, not an incremental lighting tweak.
- **Sprite import baseline**, taken from the existing sprite in
  `Assets/Sprites/Quirrel_Sprites.png.meta`:
  - Pixels Per Unit: **100**
  - Pivot: **Center (0.5, 0.5)**
  - Filter Mode: **Bilinear**
  - Max Size: **2048**
  - sRGB (Color Texture): on, Alpha Is Transparency: on
  - Sprite Mode: currently **Single** (the sheet is not yet grid-sliced into
    individual frames — see Section 9)

  New sprites should match this baseline unless the whole project deliberately
  migrates (Pixels Per Unit especially — changing it after any collider/level
  geometry exists shifts every object in the game; treat it as effectively
  frozen).

  **Deviation for per-frame character sprites:** once `Quirrel_Sprites.png` is
  cut into individual per-frame sprites, those frames use **Pivot: Bottom
  (0.5, 0)** instead of the sheet-level Center pivot above. Per-frame bounding
  boxes are tight crops with varying heights frame-to-frame; a Center pivot
  would make the character bob vertically between frames, whereas a Bottom
  pivot keeps the feet glued to a constant ground line. All other baseline
  settings (PPU, filter mode, sRGB, Alpha Is Transparency) still apply to
  those frames unchanged.
- **Animation approach:** classic sprite-sheet flip-book frame animation (not
  Unity 2D Animation bone/skinning) — confirmed by the frame-labeled layout in
  `Assets/Sprites/Quirrel_Sprites.png` (`IDLE`, `WALK`, `JUMP`, `ATTACK`,
  `HURT`, `DEFEND`, `DIE`). Keep new character animation in this format for
  consistency unless a deliberate switch to bone-based animation is proposed
  and aligned on first (it would require re-authoring existing sprite-based
  clips).
- **Animation timing** (frame counts as already laid out in the current sheet —
  treat as the baseline to hit when new/replacement art comes in):

  | Verb | Frames | Notes |
  |---|---|---|
  | Idle | 4 | Slow, breathing-like loop |
  | Walk | 6 | Full step cycle |
  | Jump | 3 | Anticipation crouch → apex → descend |
  | Attack (swing) | 4 | Wind-up → extended point → recovery; pin should read as a distinct long line at peak extension |
  | Defend/Block | 3 | Raise → hold pin-head forward → recover |
  | Hurt | 1 | Snap reaction, no easing — sells the hit as sudden |
  | Die | 3 | Body drop → mask separates/shatters (matches Section 2.5 stage 4) |
  | Look Up/Down | 1 each | Cosmetic idle-only overlay, shown while W/S held and the character is idle+grounded; cancels immediately on any other action |
  | Sitting | 1 | Cosmetic idle-only overlay; entered via W near the bench prop while idle and grounded, or automatically at spawn if already seated; blocks Attack/Jump/Defend at the code level until the player walks away |

  General easing rule: anticipation and follow-through on every "heavy" verb
  (attack, land, death); hurt is the one deliberate exception (sharp, no
  ease-in) because it needs to read as a sudden interruption.
- **UI canvas resolution:** design UI at a **1920x1080** reference resolution
  with Canvas Scaler set to "Scale With Screen Size" against that reference —
  this is a desktop-only, WASD-controlled game, so no mobile safe-area
  handling is needed. Coordinate with @ui-developer before locking this in if
  UI work begins.
- **Palette discipline for future asset generation:** whether an asset is
  hand-drawn or produced with AI assistance, colors must be picked from the hex
  tables in this document, not eyeballed from a reference screenshot — visual
  drift compounds silently across dozens of assets and is expensive to notice
  and fix later.

---

## 8. Anti-Slop Guards

Explicitly **not** this game's visual language:

- No purple-cyan gradient "AI glow" on anything.
- No generic circular asset-store particle bursts for hits/impacts — VFX are
  hand-shaped ink-splatter forms (Section 6).
- No drop shadows on UI panels or icons.
- No unmotivated bloom / glow without an in-fiction light source.
- No infection-orange used for anything except the infection itself (never
  hit-flashes, never mask damage, never generic "danger" UI — see Section 2.6).
- No background detail sharp or saturated enough to compete with the player
  silhouette.
- No smooth painterly gradients on characters — that rendering language is
  reserved for backgrounds/atmosphere only (Section 3).

---

## 9. Asset Alignment Notes (current gaps to close)

These are known mismatches between this bible and what's already in the repo,
called out so the next asset pass corrects them rather than compounds them:

- **Pin head color:** `Assets/Sprites/Quirrel_Sprites.png` currently shows the
  pin head in gold/amber tones across all animation states. This bible
  specifies **green (`#6FA25C`, the "Verdant" pin)** as the default starting
  pin head (see Section 2.3). The amber look is reserved for the "Ember" tier
  upgrade instead. Needs a recolor pass on the base pin-head asset once
  confirmed with the team.
- **Hurt/Die impact flash color:** frame 29 of `Quirrel_Sprites.png` (start of
  the DIE sequence) shows an orange impact flash. Per Section 2.6, saturated
  orange is reserved exclusively for infection and must not double as a
  generic hit-flash color — recommend replacing it with a pale/near-white
  impact flash to avoid confusing "I got hit" with "this is infected."
- **Sprite Mode is currently "Single":** the sheet in
  `Assets/Sprites/Quirrel_Sprites.png` is one flat image, not yet grid-sliced
  into individual frame sprites (Sprite Mode: Multiple + grid/cell slicing).
  This is a pipeline task for whoever wires up the Animator — flagging here so
  it isn't mistaken for already-usable frame data.
- Mask crack stages (Section 2.5, stages 1–3) do not exist yet as distinct
  frame art — only the base (stage 0) and shatter (stage 4, DIE frames 29–34)
  are represented in the current sheet. These need to be authored/commissioned
  before the health-feedback system in Section 2.5 can be fully implemented.
- **HUD vs. diegetic health display (Docs/Plans/010_health-mask-system.md):**
  the upper-left Mask-pip row (`MaskHUD`, Section 2.4) ships now as a
  conventional floating HP-bar-style HUD. Section 4.1's diegetic on-character
  mask-crack system (Section 2.5) remains deliberately unbuilt — the two are
  additive, not in conflict; the HUD does not replace or contradict the
  longer-term diegetic direction, it fills the gap until that system exists.

---

## 10. How Other Collaborators Use This Doc

- **@gameplay-programmer** — Section 2.5 (mask crack stage triggers vs. HP),
  Section 4.2 (pin point/head hitbox-relevant frames), Section 7 (animation
  frame counts/timing).
- **@ui-developer** — Section 2.4 (UI/accent palette), Section 6 (VFX/UI
  restraint rules), Section 7 (canvas resolution guidance).
- **@audio-designer** — Section 4.1/4.2/2.5 for impact beats that need to land
  with sound (mask crack steps, shatter, parry-flash timing).
- **@qa-engineer** — Section 3 (silhouette/contrast rules) as a regression
  checklist; Section 9 as known open issues, not new bugs.
- **@build-engineer** — large binary art assets (sprite sheets, source art)
  should go through Git LFS; this doc doesn't prescribe folder structure, only
  visual content.

When in doubt: silhouette first, desaturated base, color only when it means
something. If a change requires breaking a rule in this document, propose the
change and align on it here before implementing — don't drift quietly.
