# CLAUDE.md — Project Workflow

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
