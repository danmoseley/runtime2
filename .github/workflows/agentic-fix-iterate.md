---
description: "Iterate on an existing PR based on review feedback — re-implement fix from scratch with improvements"

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
1. **Issue number** from the PR title (e.g., "Fix dotnet/runtime#124968" → 124968)
2. **Files changed** — read these carefully, this is the original fix you must reproduce AND improve
3. **The diff content** — understand exactly what was changed so you can re-apply it with fixes
4. **Review comments** — find the aggregator synthesis comment (`<!-- gh-aw-review-aggregator -->`) for structured feedback
5. **PR number** — you'll reference this in your new PR

**Fix instructions from dispatch:**
```
${{ inputs.fix_instructions }}
```

## Phase 1: Setup Golden Build (1 TURN)

> **IMPORTANT:** You work from the default branch (`main`), NOT the PR branch. AWF's `create_pull_request` always generates patches against `main`, so your changes must be applied on top of `main`. You read the PR diff in Phase 0 to understand the original fix — now you will re-implement it with the review fixes applied.

**Run this ENTIRE block in ONE bash call:**

```bash
set -e
# Stay on main (default checkout) — do NOT checkout the PR branch

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

**If the edit tool fails** (e.g., "old_str not found" or ambiguous match): include MORE surrounding context lines in `old_str` to make the match unique. If editing a ref assembly file with similar method signatures, include the full method signature plus 1-2 lines above/below. As a last resort, read the file, determine the line range, and rewrite the entire section.

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

Use `create_pull_request` to push your changes. AWF will create a new branch and PR automatically.

> **KEY:** You started from `main` and re-implemented the original fix + review fixes. The old PR (#${{ inputs.pr_number }}) will be superseded by this new one. Reference it in the body.

**PR title:** Same as the original PR title (copy from Phase 0).

**PR body:**
```markdown
## Iteration: Review Feedback Applied

Supersedes #${{ inputs.pr_number }} with review feedback addressed:
- [list each fix applied]

**Self-review:** Correctness ✅/⚠️ | Tests ✅/⚠️

> [!NOTE]
> This update was generated by the Fix Iteration Agent based on review feedback.
```

After `create_pull_request`, also call `add_comment` on the OLD PR (#${{ inputs.pr_number }}) with body:
```
Superseded by #<new-PR-number> which incorporates review feedback. Closing this PR.
```

Do NOT call `add_labels` — the review aggregator will apply labels after reviewing.

## Rules

- Work from `main` — do NOT checkout the PR branch. AWF patches are always generated against `main`.
- Re-implement the ENTIRE fix from scratch (original + review fixes), not just the delta.
- Do NOT use `Fixes #` or `Closes #` in the PR body.
- Keep changes focused on the original issue + review feedback — don't refactor beyond what's asked.
- Reference the original PR number in all commits and the PR body.
- **NEVER finish without calling `create_pull_request` or `noop`.** A text-only final response will hang the workflow.
