# Agentic Bug-Fixing Pipeline — Roadmap & Status

Shared live doc. Dan can add ideas/notes here; Copilot keeps status current.
Companion to `design.md`.

**Testing repo:** `danmoseley/runtime2` (non-fork, avoids AWF push_to_pull_request_branch fork limitation)

### Setup Required for runtime2
- [x] `gh aw init` — repo initialized
- [ ] **COPILOT_GITHUB_TOKEN** — must be a fine-grained PAT (`github_pat_...`), NOT an OAuth token. Copy from danmoseley/runtime repo secrets → runtime2 repo secrets. Go to https://github.com/danmoseley/runtime2/settings/secrets/actions
- [x] Workflows pushed (main + agentic/infra synced)

## Pipeline Flow

```
Human kicks off selector (GitHub Actions UI or gh CLI)
  ↓
Selector → picks issues → auto-dispatches fixer(s)
  ↓
Fixer → creates PR with code + tests
  ↓ (pull_request: opened)
Code Review + API Review (parallel, ~3-20 min)
  ↓ (workflow_run → dispatch-aggregator.yml)
Review Aggregator → synthesizes, labels, posts fix instructions
  ↓ (workflow_run → dispatch-iteration.yml, checks ai:needs-iteration label)
Iteration Agent → applies fixes to same PR (push_to_pull_request_branch)
  ↓ (pull_request: synchronize)
Reviews fire again → Aggregator → loop until satisfied
  ↓
Aggregator sets ai:ready-for-human → human reviews final PR
```

## What's Working (validated through experiments)

- [x] **Golden Build** — weekly, uploads clr+libs artifacts as GitHub Release
- [x] **Issue Selector** — picks api-approved issues, area- prefix handling, exclusion list
- [x] **Fixer Agent** — creates PRs with code + tests (~7-10 min for easy issues)
- [x] **Code Review** — thorough, catches real bugs (~9-21 min, hits AWF timeout sometimes)
- [x] **API Surface Review** — fast, excellent quality (~3 min)
- [x] **Review Aggregator** — synthesizes, deduplicates, sets labels (~3 min)
- [x] **Labels** — all 14 `ai:*` labels created in fork
- [x] **safe_outputs** — fixer/iterate create PRs, reviews post comments, aggregator sets labels

## Current Sprint: Smooth Single-Issue Loop

**Goal**: One issue flows through selector → fixer → reviews → aggregator → iteration → re-review → done, fully automated within GitHub, no human in the loop until `ai:ready-for-human`.

### Active Work
- [x] **Validate iteration agent** — `push_to_pull_request_branch` works on non-fork repos (runtime2). **Blocked on fork repos** (`head.repo.fork=true`).
- [x] **Wire Reviews → Aggregator trigger** — `dispatch-aggregator.yml` fires on code-review/api-review completion
- [x] **Wire Aggregator → Iteration trigger** — `dispatch-iteration.yml` fires on aggregator completion (both success+failure)
- [x] **Wire Fixer → Reviews trigger** — `review-orchestrator.yml` fires on fixer completion, finds PR by `ai:agent-fix` label
- [ ] **Full pipeline end-to-end** — In progress: PR #6 (issue #124968) running through fixer → reviews → aggregator → iteration
- [ ] **Exit condition** — aggregator recognizes "no blocking issues" and sets `ai:ready-for-human` instead of looping

### Issues Fixed (2026-03-29)
- **Aggregator `add_comment` missing `item_number`** — added explicit JSON examples in prompt (Step 5)
- **Iteration MCP session timeout** — tightened to 18-min constraint, build-once rule, 200+ line reads
- **dispatch-iteration skipping on aggregator failure** — now fires on both success AND failure conclusions
- **Fixer deduplication false positive** — fixer noop'd on #103468 because closed PR #5 existed; used fresh issue #124968

### Automation Triggers (implemented)
`workflow_run` fires for AWF workflows triggered by real `pull_request` events. `workflow_dispatch` works with GITHUB_TOKEN since Sep 2022.

| Trigger | Workflow | Status |
|---------|----------|--------|
| Reviews → Aggregator | `dispatch-aggregator.yml` — fires on code-review/api-review completion, checks both done, dispatches aggregator | ✅ Implemented, includes race condition guard |
| Aggregator → Iteration | `dispatch-iteration.yml` — fires on aggregator completion, checks `ai:needs-iteration` label, dispatches iteration | ✅ Implemented, PR extraction via display_title + comment fallback |

## Backlog — Near Term

- [ ] **Remove library/test_project from fixer inputs** — fixer discovers them from issue context (namespace → library → test project). Selector then only outputs issue numbers.
- [ ] **Re-enable CI** — once iteration agent can respond to CI failures
- [ ] **Re-enable auto-dispatch** (selector → fixer chain) — disabled to reduce noise during single-issue focus
- [ ] **Inline code review instructions** — code review loads SKILL.md (extra turn, ~15 min overhead). Inlining could cut to ~5-10 min.
- [ ] **Max iteration cap** — prevent infinite loops (e.g., 3 iterations then `ai:failed`)

## Backlog — Future

- [ ] **Batch mode** — "fix 50 issues in IO, JSON, Xml" with parallel fixers
- [ ] **Fixer learns from reviews** — when reviewers repeatedly flag patterns (e.g., "missing try/catch in DisposeAsync"), add to fixer prompt automatically
- [ ] **Security reviewer** — injection, path traversal, unsafe patterns
- [ ] **Perf reviewer** — allocation patterns, hot path, benchmark suggestions
- [ ] **"Should we fix this?" reviewer** — is the issue real? by-design? already fixed?
- [ ] **Cost tracking** — tokens, time, $ per issue
- [ ] **Regression validation** — run test on main WITHOUT fix, confirm it fails, then with fix, confirm it passes
- [ ] **Upstream PR creation** — when `ai:ready-for-human` and human approves, auto-create PR on dotnet/runtime
- [ ] **Scheduled selector** — cron trigger picks N issues daily

## Dan's Ideas / Notes

_Add ideas here. Copilot will see them on the next session._

-

## Experiments Log

| # | Issue | Fixer | Reviews | Aggregator | Iteration | Result |
|---|-------|-------|---------|------------|-----------|--------|
| 37 | #124968 ImmutableArray.IndexOf | PR #6 ✅ | API ✅ Code ⏳ | ⏳ | ⏳ | First full chain on runtime2 |
| 36 | #103468 DirectoryNotFoundException | noop ❌ | Ran against wrong PR #1 | — | — | Fixer dedup'd (saw closed PR #5) |
| 30 | #88244 | PR #55 ✅ | API+Code ✅ | ✅ | ✅ agent correct, fork blocked | push_to_pr_branch validated |
| 29 | #88244 | PR #55 ✅ | API+Code ✅ | ✅ | ❌ noop (no git commit) | prompt fix needed |
| 28 | #88244 | PR #55 ✅ | API+Code ✅ | ✅ | ❌ noop (edit tool fail) | push_to_pr_branch infra works |
| 27+ | #88244 TextReader IAsyncDisposable | PR #55 ✅ | API+Code ✅ | ✅ | See 28-30 | Iteration testing |
| 26 | #88244 | PR #54 ✅ | API+Code ✅ | ✅ | ❌ git push failed | Iteration fix needed |
| 24 | #120596 LINQ Join | PR #51 ✅ | API+Code ✅ | ✅ | PR #53 (safe_outputs fail) | Full pipeline ~25m |
| 23 | #124968 ImmutableArray.IndexOf | PR #47 ✅ | Caught Builder bugs | ✅ | PR #49 ✅ | Full pipeline validated |
| 23 | #125363 Regex \\R | PR #48 ⚠️ | Caught 3 bugs + RTL | ✅ needs-changes | Not attempted | Complex issue |
| 22 | #103468 DirectoryNotFoundException | PR #45 ✅ | API+Code ✅ | ✅ | Not attempted | Good quality |
| 21 | #103468 | PR #44 ✅ | 7→10 findings over 2 rounds | N/A | Manual | Review iteration validated |

## Lessons Learned

1. **AWF sandbox cannot `git push`** — must use safe outputs (`create_pull_request`, `push_to_pull_request_branch`) for all code changes
2. **AWF agent hangs if no safe output tool called** — must always call a safe output or `noop`
3. **GitHub API rate limit on GHA runners** — use HTML URLs for public issue fetch, not API
4. **GITHUB_TOKEN events don't trigger other workflows** — need PAT or alternative for workflow chaining
5. **dotnet/runtime test paths are non-obvious** — System.IO types tested under System.Runtime/tests/
6. **Code review takes 3-4x longer than API review** — loads SKILL.md file (extra turn + large context)
7. **`push_to_pull_request_branch` is the right tool for iteration** — pushes to existing PR branch, preserves comments/reviews. `create_pull_request` always creates a NEW branch+PR.
8. **1 issue = 1 PR** — iteration pushes to same PR, never creates new ones
9. **Fixer quality improves with sibling pattern guidance** — "look at how TextWriter does it"
10. **When hitting AWF limitations, search docs first** — github.github.com/gh-aw/reference/ before workarounds
11. **`push_to_pull_request_branch` doesn't work on fork repos** — AWF checks `head.repo.fork` flag. Testing on danmoseley/runtime (fork) must use `create_pull_request`. Production on dotnet/runtime will work.
12. **MCP session timeout ~25 min** — safe_outputs MCP server session expires. Agents must complete and call push within 18 min or lose all work.
13. **Fixer deduplication is aggressive** — if any commits reference the issue (even on closed PRs), fixer noop's. Use fresh issues for testing.
14. **Aggregator needs explicit JSON examples** — prose instructions alone insufficient for `add_comment` with `item_number` field. Agent follows JSON format examples reliably.

## Key Files

| File | Purpose |
|------|---------|
| `.github/workflows/agentic-fix-issue.md` | Fixer agent prompt |
| `.github/workflows/agentic-fix-iterate.md` | Iteration agent prompt |
| `.github/workflows/code-review.md` | Code reviewer |
| `.github/workflows/api-review.md` | API surface reviewer |
| `.github/workflows/review-aggregator.md` | Review synthesizer + labels |
| `.github/workflows/agentic-issue-selector.md` | Issue picker |
| `.github/workflows/auto-dispatch-fixers.yml` | Selector → fixer chain (disabled) |
| `.github/workflows/ci-build-test.yml` | CI build+test (disabled) |
| `.agentic/docs/design.md` | Architecture design doc |
