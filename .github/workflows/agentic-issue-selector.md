---
description: "Select issues from upstream dotnet/runtime that are good candidates for automated fixing"

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
    toolsets: [default, search]
  web-fetch:

checkout:
  fetch-depth: 1

safe-outputs:
  create-issue:
    max: 1

on:
  workflow_dispatch:
    inputs:
      areas:
        description: 'Comma-separated area labels to focus on (e.g., area-System.Text.Json,area-System.Collections)'
        required: true
        type: string
      max_issues:
        description: 'Maximum number of issues to select'
        required: false
        type: number
        default: 5
      upstream_repo:
        description: 'Upstream repo (default: dotnet/runtime)'
        required: false
        type: string
        default: 'dotnet/runtime'
      difficulty:
        description: 'Target difficulty level'
        required: false
        type: string
        default: 'easy'
      extra_guidance:
        description: 'Additional human guidance for this batch (e.g., "avoid perf-related issues", "try some medium ones")'
        required: false
        type: string
        default: ''

engine:
  id: copilot
  model: gpt-4.1
---

# Issue Selector Agent

You are an issue selector for an AI bug-fixing pipeline targeting dotnet/runtime. Your job is to evaluate candidate issues from upstream and select ones that an AI fix agent can likely solve successfully.

## Step 1: Load Selection Criteria

Read `.agentic/skills/select-issues.md` from the repository. This contains the full selection criteria, hard filters, soft evaluation rubric, and output format.

## Step 2: Human Guidance for This Batch

Areas to focus on: **${{ inputs.areas }}**
Maximum issues to select: **${{ inputs.max_issues }}**
Target difficulty: **${{ inputs.difficulty }}**
Additional guidance: ${{ inputs.extra_guidance || 'None' }}

## Step 3: Query Candidate Issues

Search `${{ inputs.upstream_repo }}` for open issues matching these criteria:

1. Search for issues with each area label specified in `${{ inputs.areas }}`
2. Apply the hard filters from the selection skill:
   - Has repro or clear steps
   - Not in excluded complex areas (GC, CodeGen, VM, Interop)
   - No linked open PRs
   - Not labeled `ai:do-not-attempt`
3. Check soft signals (multiple area labels, age, assignment, etc.) — lower confidence but don't auto-reject
4. Prefer recent issues (last 6 months) and issues with *positive* maintainer engagement

## Step 4: Check Already-Attempted

Before selecting an issue, check this fork for:
- Existing branches named `fix/issue-NNNNN`
- Existing PRs referencing the issue number
- Skip any issue already attempted

## Step 5: Evaluate and Select

For each candidate that passes hard filters, evaluate feasibility, value, scope, controversy, and testability per the selection skill criteria.

Select up to **${{ inputs.max_issues }}** issues, prioritizing:
1. Highest confidence of AI success
2. Clearest reproduction steps
3. Most recent (fresher issues = code more likely to match)
4. Maintainer-validated (a maintainer commented or labeled it)

## Step 6: Report Results

Use the `create-issue` safe output to report your selections. Format:

```markdown
# Issue Selection Report

**Areas:** ${{ inputs.areas }}
**Candidates evaluated:** [N]
**Selected:** [N]

## Selected Issues

### 1. dotnet/runtime#NNNNN — [title]
- **Area:** [area label]
- **Confidence:** High/Medium/Low
- **Library:** [library name]
- **Test project:** [path to test csproj]
- **Reasoning:** [why this is a good candidate]
- **Assumptions:** [likely root cause, expected fix approach, code location, testability, breaking-change risk]
- **Risk factors:** [any concerns]

### 2. ...

## Rejected Candidates

| Issue | Reason | Notes |
|-------|--------|-------|
| #NNNNN | [reason code] | [brief explanation] |
| ... | ... | ... |

## Dispatch Commands

To fix these issues, run:
gh workflow run agentic-fix-issue.md --repo ${{ github.repository }} \
  -f issue_number=NNNNN \
  -f library=System.Foo \
  -f test_project=src/libraries/System.Foo/tests/...csproj
```

**Important:** Include the dispatch commands with correct `library` and `test_project` values for each selected issue. The human (or a future orchestrator) will use these to trigger the fix workflows.

## Guidelines

- **Quality over quantity.** Selecting 3 high-confidence issues is better than 5 questionable ones.
- **Follow human guidance.** The `difficulty` and `extra_guidance` inputs tell you what the human wants — respect them over default heuristics.
- **Log every rejection** with a reason — this data helps tune selection criteria.
