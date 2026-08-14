# CLAUDE.md — Project Workflow

## Environment

The user's OS is Windows. Any shell commands (in plans, docs, or run directly)
must be PowerShell syntax, not bash/POSIX (e.g. `Get-ChildItem` not `ls -la`,
`Remove-Item` not `rm`, `;` not `&&` for chaining unless conditional).

### Local tooling for image work

For sprite/screenshot diffing and regression checks (used mainly by
`art-director` and `qa-engineer`, and referenced by `implementation-plan-architect`
when writing measurable acceptance criteria for `[ART]`/`[QA]` tasks):

- **Python image stack** — Pillow, numpy, imagehash (perceptual hashing —
  cheap "did this sprite change" checks), and opencv-python are installed,
  but **only under the Python 3.13 interpreter**, not whichever `python`/
  `python3` happens to resolve first on PATH (there are multiple Python
  installs on this machine, and the default one lacks these packages).
  Always invoke via the Windows `py` launcher: `py -3.13 -c "..."` or
  `py -3.13 script.py`. Verify with
  `py -3.13 -c "import PIL, numpy, imagehash, cv2; print('OK')"` if unsure.
- **ImageMagick** — the `magick` CLI is installed and on PATH globally
  (`magick -version` to confirm). Reach for it for quick one-off image ops
  (resize, format convert, compare) instead of writing a Python script.
- **Wand** (Python ImageMagick wrapper) is also installed under the same
  Python 3.13 interpreter, if a scripted ImageMagick pipeline is needed.
- **ripgrep (`rg`)** is installed and on PATH; the built-in Grep tool is
  already backed by it, so raw `rg` in Bash is only needed for ad hoc
  pipelines the Grep tool doesn't cover.
- **`fd` and `jq` are NOT currently installed** (not found on PATH or in
  common install locations as of 2026-08-05). Don't assume they're
  available — use Glob/PowerShell for file-finding and Python's `json`
  module (or `ConvertFrom-Json`/`ConvertTo-Json` in PowerShell) for JSON
  instead, until the user installs them.

### If Unity becomes unreachable

This project relies on a live Unity Editor MCP connection (`mcp__UnityMCP__*`
tools) for anything that touches a scene, prefab, Animator controller, or
sprite import setting, and for running EditMode/PlayMode tests — static
review or a Python reimplementation of Unity's own logic is not an
acceptable substitute for these (see "Prioritize Unity's own tools" below).

If the Unity MCP tools are unavailable — not found via `ToolSearch`, the
connection errors out, calls time out, or the Editor is otherwise
unresponsive — for you (the orchestrator) or for any subagent:

1. **Stop.** Do not fall back to static analysis, a reimplementation in
   Python, or any other workaround to "get the task done anyway" — that
   produces unverified work that looks verified (see
   `Docs/Plans/008_bench-sit-mechanic.md`'s history for a real example of
   this going wrong).
2. **Do not guess or retry indefinitely.** A couple of reasonable retries
   (e.g. re-running `ToolSearch`, one `refresh_unity` attempt) are fine, but
   don't loop on it.
3. **Post a clearly-flagged message in the chat** so the user notices it:
   literally include the line `MARIA, RESTART UNITY` (the user's name),
   plus one sentence on what you were doing when it happened, so work can
   resume from the right place once the Editor is back.
4. Leave any in-progress edits in a safe, reviewable state (don't leave a
   half-applied multi-file change) and wait for the user's response rather
   than continuing on other tasks that also need Unity.

### Prioritize Unity's own tools

For any check that Unity itself can answer authoritatively — compiling,
running EditMode/PlayMode tests, inspecting/mutating a scene or Animator
controller, verifying a sprite's import settings or pivot, entering Play
Mode — use the live Unity Editor MCP tools first, not a Python
reimplementation or static file reading. Python (`py -3.13` per the image
tooling above) and static review are for genuine gaps Unity's own tools
don't cover (perceptual-hash diffing, batch pixel measurement on a
reference image, etc.), or for the rare case where Unity is confirmed
unreachable and the "If Unity becomes unreachable" protocol above has
already been followed. Do not treat a Python port of Unity's own C# logic
as equivalent to actually running that logic in the Editor — a Python
reimplementation can silently diverge from the real tool, and the resulting
sprite/asset would then be shipped instead of the tool's own verified
output. See `Docs/Plans/008_bench-sit-mechanic.md`'s implementation history
for a concrete case where this happened and had to be redone.

## Task pipeline (required for all work)

Every task in this project — whether it comes from the user directly or is
discovered during other work (e.g. an art-alignment gap, a bug found during
QA) — must go through this pipeline before any implementation agent touches
code or assets:

1. **Draft** — Send the task/prompt to the `implementation-plan-architect`
   agent. It writes an implementation plan: broken into 1–4 hour tasks, with
   acceptance criteria and regression impact noted.
2. **Review** — The plan goes to `implementation-plan-reviewer` for
   completeness, task granularity, regression impact, and technical
   correctness. Iterate between architect and reviewer until the reviewer
   accepts the plan. Do not skip this step, even for small tasks.
3. **Commit the plan** — Once accepted, write the plan to a local file under
   `Docs/Plans/` (one file per plan). This is the durable record — Claude
   Code's built-in session task list (`TaskCreate`) is ephemeral and does not
   persist across sessions, so plans must live in a file, not just in-session
   tasks.
   - **Naming convention:** plan files are prefixed with a sequential 3-digit
     counter, e.g. `001_pin-head-recolor.md`, `002_die-flash-fix.md`. Check
     the highest existing prefix in `Docs/Plans/` before creating a new plan
     file and increment from there — don't reuse or guess a number.
4. **Git commit and push** the plan file before implementation starts.
5. **Implement** — Only after the plan file is committed and pushed do
   implementation agents (`gameplay-programmer`, `ui-developer`,
   `art-director`, `audio-designer`, `game-data-engineer`, `qa-engineer`,
   `build-engineer`, etc.) begin work on the accepted plan.

Do not write task descriptions only to the in-session task list — that list
is fine for tracking progress *within* a session, but the plan itself must
exist as a committed file in `Docs/Plans/` before implementation starts.

## Delegate to the human when that's cheaper

Before drafting or implementing a plan, check whether the task is much less
effort for the user to just do by hand than to have agents plan and execute
it (e.g. recoloring one element in Photoshop/Aseprite, renaming a file,
tweaking a single Inspector value). If so, **say so and hand it back to the
user instead of running it through the full pipeline.**

**Why:** the pipeline's rigor (multi-round architect/reviewer cycles, pixel-
level regression proofs, dependency-graph verification) has real token cost.
For a genuinely simple visual tweak, that cost can dwarf the 5 minutes it
would take the user to open the file and change it themselves — burning
their quota on process for a task with no real regression risk.

**How to apply:** when scoping a task (as architect, reviewer, or the routing
agent), if the actual work is a small, manual, low-risk edit a human can just
do directly — say so plainly and offer that as the default, rather than
defaulting to the full agent pipeline. Reserve the full pipeline for work
that's genuinely risky, multi-file, or tedious enough that automation earns
its cost.
