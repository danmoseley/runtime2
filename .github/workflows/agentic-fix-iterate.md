---
description: "Iterate on an existing PR based on review feedback — push fixes to the PR branch"

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
  fetch: ["*"]
  fetch-depth: 0

safe-outputs:
  push-to-pull-request-branch:
    target: "*"
    labels: [ai:agent-fix]
    max: 1
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
> - **TIMING:** Call `push_to_pull_request_branch` within 18 minutes. Safe outputs MCP session expires ~25 min. If you exceed this, YOUR WORK IS LOST.
> - **NEVER NARRATE.** Every response MUST include at least one tool call.
> - **NEVER DELEGATE TO SUB-AGENTS.** No Explore agents, background agents, or task delegation.
> - **DO NOT READ** doc files (`.agentic/skills/`, `CONTRIBUTING.md`, etc.). Guidelines are inlined below.
> - **MANDATORY OUTPUT:** Call `push_to_pull_request_branch` or `noop` before finishing.
> - **NEVER** run `./build.sh` or `./build.cmd`. Build individual projects via `./eng/common/dotnet.sh build <csproj>`.
> - **READ FILES EFFICIENTLY:** Always read 200+ lines at a time. Never read in 60-line chunks.
> - **BUILD ONCE:** Only build/test ONCE at the end. Do NOT build after each edit.

## Phase 0: Read PR Context (1 TURN)

**Batch ALL of these in a single turn:**
- `web-fetch: https://api.github.com/repos/${{ github.repository }}/pulls/${{ inputs.pr_number }}`
- `web-fetch: https://api.github.com/repos/${{ github.repository }}/pulls/${{ inputs.pr_number }}/files`
- `web-fetch: https://api.github.com/repos/${{ github.repository }}/issues/${{ inputs.pr_number }}/comments?per_page=100`

From the PR metadata, extract:
1. **Issue number** from the PR title (e.g., "Fix dotnet/runtime#124968" → 124968)
2. **Files changed** — read these carefully, this is the original fix you must reproduce AND improve
3. **The diff content** — understand exactly what was changed so you can re-apply it with fixes
4. **Review comments** — find the aggregator synthesis comment (`<!-- gh-aw-agentic-workflow: Review Aggregator`) for structured feedback
5. **PR number** — you'll reference this in your new PR

**Fix instructions from dispatch:**
```
${{ inputs.fix_instructions }}
```

## Phase 1: Checkout PR Branch + Golden (1 TURN)

> **IMPORTANT:** You work on the PR branch. `push_to_pull_request_branch` will push your commits directly to the existing PR's branch.

**Run this ENTIRE block in ONE bash call:**

```bash
set -e
# Checkout the PR branch so changes are applied on top of the existing fix
PR_BRANCH="<branch-name-from-phase-0>"
git fetch origin "$PR_BRANCH"
git checkout "$PR_BRANCH"

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

**SPEED IS CRITICAL.** You have ~18 minutes total. Don't explore — act. You already have the fix instructions and the PR diff from Phase 0. Apply the edits directly.

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

**If the edit tool fails** (e.g., "old_str not found" or ambiguous match): include MORE surrounding context lines in `old_str` to make the match unique. If editing a ref assembly file with similar method signatures, include the full method signature plus 1-2 lines above/below. As a last resort, read the file, determine the line range, and rewrite the entire section.

Do NOT re-read files you already have context for from Phase 0.

## Phase 3: Build + Test + Commit (3 TURNS)

**Build ONCE only.** Do not rebuild after each edit — apply all edits first, then build once.

```bash
# Build the library
./eng/common/dotnet.sh build <PATH_TO_SRC_CSPROJ> -c Release
# Build + run tests
./eng/common/dotnet.sh build <PATH_TO_TEST_CSPROJ> /t:Test -c Release
```

If tests fail, fix and retry (max 1 retry). After 2 build failures, call `push_to_pull_request_branch` with whatever you have — partial progress is better than losing everything to a session timeout.

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

## Phase 4: Commit and Push Changes to PR (1 TURN)

> **CRITICAL:** You MUST `git add` and `git commit` your changes BEFORE calling `push_to_pull_request_branch`. The safe output generates a patch from your git commits — if you only edit files without committing, there is nothing to push and it will report "no changes".

**Step 1: Commit your changes (bash):**
```bash
git add -A
git status  # verify files are staged
git commit -m "Address review feedback for PR #${{ inputs.pr_number }}"
git log --oneline -3  # verify the commit exists
```

**Step 2: Call `push_to_pull_request_branch`** with:
- **pr_number:** `${{ inputs.pr_number }}`

This pushes your new commit(s) to the existing PR's branch. Same PR, same comments, same review history.

**Step 3: Call `add_comment`** on the PR with a summary:
```markdown
## Iteration: Review Feedback Applied

**Review feedback addressed:**
- [list each fix applied]

**Self-review:** Correctness ✅/⚠️ | Tests ✅/⚠️

> [!NOTE]
> This update was generated by the Fix Iteration Agent based on review feedback.
```

Do NOT call `add_labels` — the review aggregator will apply labels after reviewing.

## Rules

- Checkout the PR branch and make changes ON TOP of the existing fix.
- **You MUST `git commit` changes before calling `push_to_pull_request_branch`.** No commit = no push = wasted run.
- Use `push_to_pull_request_branch` (not `create_pull_request`) — this pushes to the existing PR.
- Do NOT use `Fixes #` or `Closes #` in comments.
- Keep changes focused on the review feedback — don't refactor beyond what's asked.
- **NEVER finish without calling `push_to_pull_request_branch` or `noop`.** A text-only final response will hang the workflow.
