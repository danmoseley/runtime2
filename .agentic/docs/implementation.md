# Agentic Bug-Fixing Pipeline — Roadmap & Status

Shared live doc. Dan can add ideas/notes here; Copilot keeps status current.
Companion to `design.md`.

## Pipeline Flow

```
Human kicks off selector (GitHub Actions UI or gh CLI)
  ↓
Selector → picks issues → auto-dispatches fixer(s)
  ↓
Fixer → creates PR with code + tests
  ↓ (pull_request: opened)
Code Review + API Review (parallel, ~3-20 min)
  ↓ (GAP: needs automation trigger)
Review Aggregator → synthesizes, labels, posts fix instructions
  ↓ (GAP: needs automation trigger)
Iteration Agent → applies fixes to same PR
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
- [ ] **Validate iteration agent** — now uses `push_to_pull_request_branch` to push to existing PR (testing run 23703071880 on PR #55)
- [ ] **Wire Reviews → Aggregator trigger** — currently manual dispatch; need automation
- [ ] **Wire Aggregator → Iteration trigger** — currently manual dispatch; need automation
- [ ] **Exit condition** — aggregator recognizes "no blocking issues" and sets `ai:ready-for-human` instead of looping

### Automation Gaps (two triggers needed)
| Gap | Options | Notes |
|-----|---------|-------|
| Reviews → Aggregator | (a) PAT-triggered reviews so `workflow_run` fires, (b) scheduled polling workflow, (c) `repository_dispatch` | GITHUB_TOKEN events don't trigger other workflows |
| Aggregator → Iteration | Aggregator dispatches `agentic-fix-iterate` via GH API | Aggregator knows the fix instructions to pass |

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
| 26+ | #88244 TextReader IAsyncDisposable | PR #55 ✅ | API+Code ✅ | Pending | Pending | In progress |
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
