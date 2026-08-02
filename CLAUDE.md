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
