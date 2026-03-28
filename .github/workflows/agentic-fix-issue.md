---
description: "Fix a dotnet/runtime issue: create branch, implement fix, run CI, review, create PR"

permissions:
  contents: read
  issues: read
  pull-requests: read

network:
  allowed:
    - defaults
    - dotnet

tools:
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
  add-comment:
    max: 10
  add-labels:
    max: 10
  create-issue:
    max: 1

on:
  workflow_dispatch:
    inputs:
      issue_number:
        description: 'Upstream dotnet/runtime issue number to fix'
        required: true
        type: number
      library:
        description: 'Library name (e.g., System.Text.Json)'
        required: true
        type: string
      test_project:
        description: 'Test project path relative to repo root (e.g., src/libraries/System.Text.Json/tests/System.Text.Json.Tests/System.Text.Json.Tests.csproj)'
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
---

# Fix Issue Agent

You are an automated bug-fixing agent for dotnet/runtime. Your job is to fix issue **${{ inputs.upstream_repo }}#${{ inputs.issue_number }}** in the library **${{ inputs.library }}**.

You are running in a personal fork of dotnet/runtime. The repo is checked out and golden build artifacts are available via GitHub Releases.

## Phase 1: Understand the Issue

1. **Read the upstream issue** at `${{ inputs.upstream_repo }}#${{ inputs.issue_number }}`. Read the full description and ALL comments.
2. Extract:
   - What is the bug? (expected vs. actual behavior)
   - Reproduction steps or code
   - Any hints from maintainers about root cause or preferred approach
   - The `area-*` label (should match `${{ inputs.library }}`)

3. **Mine git history** for context:
   ```bash
   git log --oneline -20 -- src/libraries/${{ inputs.library }}/src/
   ```
   Check if this could be a regression from a recent commit. Also search for related merged PRs in upstream: `gh pr list --repo ${{ inputs.upstream_repo }} --state merged --search "<method or type name>" --limit 5`

4. **Deduplication check:** Before any code work, search for existing PRs that reference this issue:
   - In this fork: `gh pr list --search "${{ inputs.issue_number }}"`
   - In upstream: `gh pr list --repo ${{ inputs.upstream_repo }} --search "${{ inputs.issue_number }}"`
   - Check for branches named `fix/issue-${{ inputs.issue_number }}`
   If an open PR or active branch already exists, stop early with `ai:rejected-early` and note the existing work.

## Phase 2: Read the Guidelines

Before writing any code, read these files from the repo:
- `CONTRIBUTING.md`
- `docs/coding-guidelines/coding-style.md`
- `docs/workflow/building/libraries/README.md` (or the appropriate build doc)
- `.agentic/skills/fix-issue.md` — detailed fix agent instructions

Follow the conventions described in these documents.

## Phase 3: Download Golden Build

**CRITICAL: You MUST download golden build artifacts before attempting any build or test.** The golden build provides the pre-built CLR, shared framework, and testhost. Without it, you cannot run tests. **Do NOT run `build.sh` with `clr` or `clr+libs` — that takes 40+ minutes and will fail due to missing native dependencies.**

```bash
# Download golden build artifacts
GOLDEN_TAG=$(gh release list --repo ${{ github.repository }} --limit 1 --json tagName -q '.[0].tagName')
echo "Using golden release: $GOLDEN_TAG"
gh release download "$GOLDEN_TAG" --repo ${{ github.repository }} --pattern 'golden-part-*' --dir /tmp
cat /tmp/golden-part-* | zstd -d | tar xf - -C .
echo "Golden artifacts extracted. Contents of artifacts/bin/testhost/:"
ls artifacts/bin/testhost/ 2>/dev/null || echo "WARNING: no testhost found"
```

After extraction, verify you see `artifacts/bin/testhost/net*-linux-Release-x64/dotnet`. This is required for running tests.

## Phase 4: Investigate, Hypothesize, and Test First

1. **Create a fix branch:**
   ```bash
   git checkout -b fix/issue-${{ inputs.issue_number }} origin/main
   ```

2. **Locate relevant code** in `src/libraries/${{ inputs.library }}/`:
   - Search for types and methods mentioned in the issue
   - Read full source files — understand the context, not just the bug site
   - Check callers, base classes, interfaces
   - Use `git blame` on the relevant code paths to understand recent changes

3. **Formulate your hypothesis** — before writing any code, state clearly:
   - What you believe the bug is (mechanism, location)
   - What the fix should be
   - What a failing test would look like

4. **Write the test FIRST** in the test project at `${{ inputs.test_project }}`:
   - Write a test that reproduces the bug described in the issue
   - Follow existing test patterns in the file (xUnit, naming, assertion style)
   - Test the specific scenario from the issue

5. **Run the test on current main — it MUST fail:**
   ```bash
   # Build the library (uses golden testhost for running tests)
   ./eng/common/dotnet.sh build src/libraries/${{ inputs.library }}/src/*.csproj -c Release
   ./eng/common/dotnet.sh build ${{ inputs.test_project }} /t:Test -c Release \
     /p:XUnitMethodName=YourNewTestName
   ```
   If the test **passes**, the bug is already fixed → stop early with `ai:rejected-early`. This saves an entire run's worth of effort.

6. **Implement the fix:**
   - Minimal change — fix the reported bug, nothing more
   - Match surrounding code style and patterns
   - Handle edge cases (null, empty, boundary values)
   - No breaking changes to public API

7. **Verify the test now passes** with your fix applied.

8. **Commit** with a clear message:
   ```
   Fix <brief description>

   Related: ${{ inputs.upstream_repo }}#${{ inputs.issue_number }}
   ```

9. **Diff-size check:** Run `git diff --stat origin/main` and count changed files (excluding test files). If you've modified more than **5 source files**, stop and critically re-evaluate — you may be refactoring instead of fixing. If the fix genuinely requires broad changes, note it as `ai:longer-review`.

## Phase 5: Full Test Suite

Run the **complete** test suite for the library (not just your new test):

```bash
# Build the changed library (Release)
./eng/common/dotnet.sh build src/libraries/${{ inputs.library }}/src/*.csproj -c Release

# Run full tests (Release)
./eng/common/dotnet.sh build ${{ inputs.test_project }} /t:Test -c Release
```

**Debug build/test is optional.** If the golden build includes a Debug testhost (check for `artifacts/bin/testhost/net*-linux-Debug-x64/dotnet`), also run:

```bash
./eng/common/dotnet.sh build src/libraries/${{ inputs.library }}/src/*.csproj -c Debug
./eng/common/dotnet.sh build ${{ inputs.test_project }} /t:Test -c Debug
```

If the Debug testhost does not exist, skip Debug — Release-only validation is sufficient.

**If build/tests fail:**
- Read the error output carefully
- Fix the issue in your code
- Rebuild and retest
- You have up to 3 attempts before moving to the review phase or abandoning

Confirm your new test name appears as `Passed` (not `Skipped`) in the test output — `[ConditionalFact]` tests can be silently skipped.

## Phase 6: Self-Review

Before creating the PR, review your own changes against these criteria:

1. **Correctness:** Does the fix address the root cause, not just the symptom?
2. **Tests:** Would your test fail on unmodified main? (This is critical — a test that passes without the fix proves nothing.)
3. **Breaking changes:** Did you change any public API signatures or observable behavior for correct inputs?
4. **Performance:** Did you add allocations in a hot path? Unnecessary copies?
5. **Style:** Does your code match the surrounding code's patterns?
6. **Security:** Did you weaken input validation or introduce injection risk?

Read `.agentic/skills/review-breaking.md` and `.agentic/skills/review-perf.md` for detailed criteria.

If you find issues in your own review, fix them before proceeding.

## Phase 7: Create the PR

Push your branch and create a pull request using the `create-pull-request` safe output.

**PR title:** `Fix ${{ inputs.upstream_repo }}#${{ inputs.issue_number }}: <brief description>`

**PR body format:**

```markdown
## 🤖 AI Fix: [one-line description of what was fixed]

**Issue:** ${{ inputs.upstream_repo }}#${{ inputs.issue_number }} — [issue title]
**Approach:** [1-2 sentences on what the fix does and why]
**Model:** gpt-4.1

| Aspect | Verdict | Note |
|--------|---------|------|
| Correctness | ✅/⚠️ | [brief] |
| Security | ✅/⚠️ | [brief] |
| Breaking change | ✅/⚠️ | [brief] |
| Performance | ✅/⚠️ | [brief] |
| Style | ✅/⚠️ | [brief] |
| Tests | ✅/⚠️ | [brief] |

**Labels:** [confidence and characteristic labels]

<details>
<summary>🔧 Fix agent reasoning (click to expand)</summary>

### Root Cause Analysis
[What you found]

### Approach
[What you changed and why]

### Test Strategy
[What tests you added and what they validate]

### Investigation Notes
[Anything else useful]

</details>
```

## Phase 7: Label the PR

Apply labels using the `add-label` safe output:

- **Always:** One of `ai:ready-for-human`, `ai:failed`, or `ai:rejected-early`
- **Confidence:** `ai:high-confidence`, `ai:medium-confidence`, or `ai:low-confidence`
- **Characteristics** (as applicable): `ai:quick-review`, `ai:needs-domain-expertise`, `ai:longer-review`, `ai:has-perf-concern`, `ai:has-breaking-concern`, `ai:needs-broader-tests`, `ai:alternative-suggested`

## Phase 8: Create Process Log Issue

Create a companion issue in this fork using the `create-issue` safe output:

**Title:** `[Process Log] Fix for ${{ inputs.upstream_repo }}#${{ inputs.issue_number }}: <description>`

**Body:** Structured log of what happened — issue selection reasoning, iterations, CI results, review verdicts, timing, outcome. This is for batch analysis, not human review of the fix.

## Important Rules

- **Do NOT use `Fixes #` or `Closes #`** in the PR body — that would close the upstream issue prematurely.
- **Do NOT modify files outside `src/libraries/${{ inputs.library }}/`** unless absolutely necessary.
- **Do NOT create the PR if you have zero confidence in the fix.** It's better to abandon (create an empty-commit PR with `ai:failed` label explaining why) than to submit a wrong fix.
- **If you cannot fix the issue after 3 build/test attempts**, abandon: create an empty-commit PR (`git commit --allow-empty -m "Unable to fix: <reason>"`) with the `ai:failed` label and a detailed explanation of what you tried.
