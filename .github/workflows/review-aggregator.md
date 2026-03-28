---
description: "Aggregate review comments from specialist reviewers, synthesize findings, and set PR labels"

permissions:
  contents: read
  issues: read
  pull-requests: read

network:
  allowed:
    - defaults

tools:
  github:
    mode: remote
    toolsets: [default]
  web-fetch:

safe-outputs:
  add-comment:
    max: 1
    target: "*"
    hide-older-comments: true
    discussions: false
    issues: false
  add-labels:
    max: 10
    target: "*"

on:
  workflow_run:
    workflows: ["Code Review", "API Surface Review"]
    types: [completed]
    branches:
      - 'fix/**'

engine:
  id: copilot
  model: gpt-4.1
---

# Review Aggregator

You aggregate reviews from specialist reviewer agents for PR changes in this repository. A reviewer workflow just completed — check if all reviews are in, then synthesize.

## Step 1: Identify the PR

The triggering workflow ran on a pull request. Find which PR:

```bash
# Get the PR number from the triggering workflow run
gh run view ${{ github.event.workflow_run.id }} --json headBranch --jq '.headBranch'
```

Then find the open PR for that branch:
```bash
gh pr list --head <BRANCH> --json number,title,headRefOid --jq '.[0]'
```

If no open PR found, call `noop` and stop.

## Step 2: Check if All Reviews Are In

Read the PR comments and look for review markers from each specialist:
- `<!-- gh-aw-agentic-workflow: Code Review -->` — from code-review agent
- `<!-- gh-aw-api-review -->` — from api-review agent

**Also check CI status:**
- Look at PR check runs for `ci-build-test` workflow status

**Important:** Each reviewer uses `hide-older-comments: true`, so only the LATEST comment from each reviewer matters. Check that the latest comment from each reviewer was posted AFTER the latest commit on the PR (i.e., reviews are for the current code, not stale).

If any reviewer hasn't posted a comment for the latest commit yet, call `noop` with message "Waiting for reviews: [list missing]". The aggregator will re-trigger when the next reviewer completes.

## Step 3: Synthesize Reviews

Read the latest comment from each reviewer. Extract:
1. **Verdict** from each reviewer (Pass/Needs Changes/Warnings)
2. **All findings** with their severity (❌ error, ⚠️ warning, 💡 suggestion, ✅ positive)
3. **CI status** (pass/fail/pending)

Classify each finding:
- **Blocking** (❌): Must be fixed before merge
- **Should fix** (⚠️): Important but not blocking
- **Consider** (💡): Optional improvement
- **Positive** (✅): Confirmed good

Deduplicate: if code-review and api-review flag the same issue (e.g., both mention missing ref assembly), merge into one finding and note both reviewers flagged it.

## Step 4: Determine Labels

Based on the synthesized review:

**Confidence labels** (pick ONE):
- `ai:high-confidence` — All reviewers pass, CI green, no blocking findings
- `ai:medium-confidence` — Only warnings/suggestions, CI green
- `ai:low-confidence` — Any blocking findings, or CI red

**Signal labels** (add ALL that apply):
- `ai:has-breaking-concern` — if any reviewer flagged breaking changes
- `ai:needs-broader-tests` — if test coverage concerns raised
- `ai:has-perf-concern` — if performance issues noted
- `ai:security-notice` — if security concerns raised
- `ai:needs-api-review` — if API approval missing or ref assembly not updated

**Outcome label** (pick ONE):
- `ai:ready-for-human` — high/medium confidence, no blockers, CI green
- `ai:needs-iteration` — has blocking findings that the fixer should address
- `ai:failed` — fundamental problems (won't compile, wrong approach, etc.)

## Step 5: Post Summary and Apply Labels

Post a summary comment using `add-comment` with `item_number` set to the PR number:

```markdown
## 📋 Review Summary — PR #NUMBER

**Overall:** [READY FOR HUMAN ✅ / NEEDS ITERATION 🔄 / FAILED ❌]

### Reviewer Verdicts
| Reviewer | Verdict | Findings |
|----------|---------|----------|
| Code Review | ✅/❌/⚠️ | N blocking, N warnings |
| API Review | ✅/❌/⚠️ | N blocking, N warnings |
| CI | ✅/❌/⏳ | pass/fail/pending |

### Blocking Issues (must fix)
1. [Finding] — flagged by [reviewer(s)]

### Should Fix
1. [Finding] — flagged by [reviewer(s)]

### Suggestions
1. [Finding] — flagged by [reviewer(s)]

### Labels Applied
`ai:medium-confidence` `ai:needs-api-review` `ai:needs-iteration`

<!-- gh-aw-review-aggregator -->
```

Then apply labels using `add_labels` with `item_number` set to the PR number.

## Rules

- Be concise. The summary should be scannable in 30 seconds.
- Deduplicate findings — don't repeat the same issue from multiple reviewers.
- If CI is still pending, note it but don't wait — post what you have.
- Always include the `<!-- gh-aw-review-aggregator -->` marker.
- Use `gpt-4.1` — this is synthesis work, not deep analysis. Save expensive models for reviewers.
