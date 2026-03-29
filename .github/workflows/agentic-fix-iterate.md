---
description: "Iterate on an existing PR based on review feedback — apply fixes and push updates"

permissions:
  contents: read
  issues: read
  pull-requests: read

network:
  allowed:
    - defaults
    - dotnet
    - github
  firewall:
    log-level: debug

tools:
  bash:
    - ":*"
  edit:
  github:
    mode: remote
    toolsets: [default, search]
    allowed-repos: ["dotnet/runtime"]
    min-integrity: approved
  web-fetch:

checkout:
  fetch-depth: 0

safe-outputs:
  create-pull-request:
    max: 1
    excluded-files: [".agentic/**"]
    draft: false
    labels: [ai:agent-fix, ai:iteration]
    github-token-for-extra-empty-commit: ${{ secrets.GH_AW_CI_TRIGGER_TOKEN }}
  add-comment:
    max: 10
  add-labels:
    max: 10
    target: "*"
  noop:
    report-as-issue: false

on:
  workflow_dispatch:
    inputs:
      pr_number:
        description: 'PR number to iterate on'
        required: true
        type: number
      fix_instructions:
        description: 'Review feedback to address (from aggregator synthesis)'
        required: true
        type: string
      upstream_repo:
        description: 'Upstream repo (default: dotnet/runtime)'
        required: false
        type: string
        default: 'dotnet/runtime'

engine:
  id: copilot
  model: gpt-4.1
  max-continuations: 3

timeout-minutes: 45
---

# Fix Iteration Agent

You are iterating on PR **#${{ inputs.pr_number }}** in this fork based on review feedback. Your job is to apply the requested fixes, verify they build and pass tests, and create an updated PR.

> **HARD CONSTRAINTS (read before doing anything):**
> - **TURN BUDGET:** ~15 model turns per continuation (3 max). Batch tool calls. Budget: **1** Phase 0, **1** Phase 1, **5** Phase 2, **3** Phase 3, **1** Phase 4.
> - **TIMING:** Call `create_pull_request` within 20 minutes. Safe outputs expire ~25 min.
> - **NEVER NARRATE.** Every response MUST include at least one tool call.
> - **NEVER DELEGATE TO SUB-AGENTS.** No Explore agents, background agents, or task delegation.
> - **DO NOT READ** doc files (`.agentic/skills/`, `CONTRIBUTING.md`, etc.). Guidelines are inlined below.
> - **MANDATORY OUTPUT:** Call `create_pull_request` or `noop` before finishing.
> - **NEVER** run `./build.sh` or `./build.cmd`. Build individual projects via `./eng/common/dotnet.sh build <csproj>`.

## Phase 0: Read PR Context (1 TURN)

**Batch ALL of these in a single turn:**
- `web-fetch: https://api.github.com/repos/${{ github.repository }}/pulls/${{ inputs.pr_number }}`
- `web-fetch: https://api.github.com/repos/${{ github.repository }}/pulls/${{ inputs.pr_number }}/files`
- `web-fetch: https://api.github.com/repos/${{ github.repository }}/issues/${{ inputs.pr_number }}/comments?per_page=100`

From the PR metadata, extract:
1. **Branch name** (head.ref) — you'll base your work on this branch
2. **Issue number** from the PR title (e.g., "Fix dotnet/runtime#124968" → 124968)
3. **Files changed** — these are the files you'll be modifying
4. **Review comments** — find the aggregator synthesis comment (`<!-- gh-aw-review-aggregator -->`) for structured feedback

**Fix instructions from dispatch:**
```
${{ inputs.fix_instructions }}
```

## Phase 1: Setup Branch + Golden (1 TURN)

**Run this ENTIRE block in ONE bash call:**

```bash
set -e
# Checkout the PR branch so we start from the existing fix
PR_BRANCH="<branch-name-from-phase-0>"
git fetch origin "$PR_BRANCH"
git checkout -b "iterate/${PR_BRANCH}" "origin/${PR_BRANCH}"

# Golden download (same as fixer — fresh runner needs artifacts)
sudo apt-get update -qq && sudo apt-get install -y -qq zstd > /dev/null 2>&1 || true
command -v zstd >/dev/null || { echo "FATAL: zstd not installed"; exit 1; }
sudo rm -rf /usr/share/dotnet /usr/local/lib/android /opt/ghc 2>/dev/null || true
sudo docker image prune --all --force 2>/dev/null || true
cd "$GITHUB_WORKSPACE"
REPO="${{ github.repository }}"
curl -sL "https://api.github.com/repos/${REPO}/releases" | python3 -c "
import sys, json
for r in json.load(sys.stdin):
    if r['tag_name'].startswith('golden-'):
        print(r['tag_name']); break
" > /tmp/golden_tag.txt 2>/dev/null
GOLDEN_TAG=""; [ -f /tmp/golden_tag.txt ] && read GOLDEN_TAG < /tmp/golden_tag.txt
[ -z "$GOLDEN_TAG" ] && { echo "FATAL: No golden release found"; exit 1; }
echo "Using: $GOLDEN_TAG"
curl -sL "https://api.github.com/repos/${REPO}/releases/tags/${GOLDEN_TAG}" | python3 -c "
import sys, json
for a in json.load(sys.stdin).get('assets', []):
    if a['name'].startswith('golden-part-'):
        print(a['name'], a['browser_download_url'])
" > /tmp/golden_assets.txt 2>/dev/null
[ -s /tmp/golden_assets.txt ] || { echo "FATAL: No assets found"; exit 1; }
while read -r name url; do echo "Downloading: $name"; curl -sL -o "/tmp/$name" "$url"; done < /tmp/golden_assets.txt
set -o pipefail
ls /tmp/golden-part-* | sort | xargs cat | zstd -d | tar xf - -C .
set +o pipefail
rm -f /tmp/golden-part-*
ls artifacts/bin/testhost/net*-linux-Release-x64/dotnet 1>/dev/null 2>&1 || { echo "FATAL: testhost not found"; exit 1; }
echo "Golden OK. Testhost found."
```

**If golden fails, `noop` immediately.**

## Phase 2: Apply Fixes (5 TURNS MAX)

Address EACH item from the fix instructions. Prioritize:
1. **Blocking issues (❌)** — these MUST be fixed
2. **Warnings (⚠️)** — fix these if straightforward
3. **Suggestions (💡)** — apply if simple, skip if complex

**Coding guidelines (same as fixer):**
- Use `var` only when type is obvious. Allman braces. `nameof(...)` for exception args.
- Prefix private fields with `_`, static with `s_`, thread-static with `t_`.
- Match the style of surrounding code — consistency trumps rules.
- All new `.cs` files MUST have the MIT license header.
- Tests: `[Fact]` or `[Theory]` with `[InlineData]`. Method names: `Method_Condition_Expected`.
- Put tests in existing test files. Do NOT create files in non-test directories.
- **Match the issue specification EXACTLY.** If the issue specifies a particular construct (e.g., atomic group, character class), use exactly that.
- If adding/changing public API, update the ref assembly.

**For each fix:**
1. Read the relevant file(s) if needed
2. Apply the edit
3. Move to the next fix

Do NOT re-read files you already have context for from Phase 0.

## Phase 3: Build + Test + Commit (3 TURNS)

```bash
# Build the library
./eng/common/dotnet.sh build <PATH_TO_SRC_CSPROJ> -c Release
# Build + run tests
./eng/common/dotnet.sh build <PATH_TO_TEST_CSPROJ> /t:Test -c Release
```

If tests fail, fix and retry (max 2 attempts). After 3 failures, `noop` with summary.

**Pre-commit checks:**
```bash
git diff --stat origin/main
```

**Commit:**
```bash
git add -A
git commit -m "Address review feedback for PR #${{ inputs.pr_number }}

Fixes applied based on aggregator synthesis:
- [list key fixes]"
```

## Phase 4: Create Updated PR (1 TURN)

Use `create-pull-request` to create a new PR. The PR supersedes the original.

**PR title:** `Fix iteration: PR #${{ inputs.pr_number }} — address review feedback`

**PR body:**
```markdown
## Iteration: Review Feedback Applied

**Original PR:** #${{ inputs.pr_number }}
**Review feedback addressed:**
- [list each fix applied]

**Changes from original:**
- [brief diff summary]

**Self-review:** Correctness ✅/⚠️ | Tests ✅/⚠️

> [!NOTE]
> This PR was generated by the Fix Iteration Agent based on review feedback.
```

Then apply labels via `add_labels` — but **ONLY AFTER calling `create_pull_request` first** (labels need the PR to exist):
- `ai:iteration` (already on PR from safe-outputs config)
- Confidence: `ai:high-confidence` / `ai:medium-confidence` / `ai:low-confidence`
- Outcome: `ai:ready-for-human` / `ai:needs-iteration`

**⚠️ ORDER MATTERS:** You MUST call `create_pull_request` BEFORE `add_labels`. If you call `add_labels` first, it will fail because there's no PR to label yet.

## Rules

- Do NOT use `Fixes #` or `Closes #` in the PR body.
- Keep changes focused on the review feedback — don't refactor beyond what's asked.
- Reference the original PR number in all commits and the PR body.
- **NEVER finish without calling `create_pull_request` or `noop`.**
