# Agentic Bug-Fixing Pipeline: Implementation Checklist

Companion to `agentic.design.md`. Tracks what's needed to get from design to first fix.

## Discovery (completed)

- [x] Fork `danmoseley/runtime` is clean, has upstream remote, Actions enabled
- [x] `gh aw` extension installed (`gh extension install github/gh-aw`)
- [x] Upstream workflows have fork guards (`github.repository == 'dotnet/runtime'`) — no need to disable them
- [x] Agentic workflow format understood (YAML frontmatter + markdown, compiled via `gh aw compile`)
- [x] Existing skills in `.github/skills/` catalogued (code-review, ci-analysis, issue-triage, etc.)

## Human Setup (Dan, one-time, ~10 min) — DONE

- [x] **`gh aw init`** — already initialized (inherited from upstream; `gh aw status` shows `code-review` and `copilot-echo` active)
- [x] **PAT secret** — `COPILOT_GITHUB_TOKEN` added as fine-grained PAT scoped to `danmoseley/runtime` with Copilot Requests (account-level), Contents, PRs, Issues, Workflows read/write. Second upstream-isolation PAT deferred to later.
- [x] **Verify agentic workflows are enabled** — confirmed via `gh aw status`
- [x] **Clean up stray files** — done (removed `test_depth2000.cs`, `test_depth2000.csproj`)

## Phase 1: Golden Build (plain GitHub Actions — no agentic needed)

**Goal:** Full build of clr+libs, compressed artifacts uploaded as a GitHub Release.

### Copilot writes:
- [x] `.github/workflows/agentic-golden-build.yml`
  - Triggers: `workflow_dispatch` + `schedule` (weekly Monday 6 UTC)
  - Steps: blobless clone, reset to upstream, re-merge `agentic/infra`, force-push, `build.sh -subset clr+libs -c Release` + `libs -c Debug`, `zstd --ultra -22`, `split` if needed, `gh release create`, delete old releases
  - Includes disk space cleanup step (removes Android SDK, Docker images, etc.)
  - Risk: disk space on `ubuntu-latest` (~14GB free, build produces ~11GB). May need larger runner.

### Validate (PoC 1):
- [ ] Trigger manually, observe: does it complete? How long? Compressed size? Needs splitting?
  - **IN PROGRESS** — run 23669709875 triggered, waiting for results
  - Previous failures: shallow clone broke merge (fixed: blobless clone), missing git identity (fixed: git config), stale force-push-with-lease race (fixed: retrigger)
- [ ] Record numbers: wall-clock, artifact size, compressed size

## Phase 2: Minimal CI (plain GitHub Actions)

**Goal:** Download golden artifacts, rebuild one library, run its tests.

### Copilot writes:
- [x] `.github/workflows/agentic-libs-test.yml`
  - Triggers: `workflow_dispatch` (inputs: `library`, `test_project`)
  - Steps: download golden release, decompress, rebuild single library, run tests in the specified `test_project`
  - Runs Release and Debug in parallel (two jobs)
  - Uses SHA-pinned `actions/checkout`
  - Also: `validate_regression` mode (run test on main without fix, confirm failure) — **deferred to later iteration**

### Validate (PoC 2):
- [ ] Run against an unchanged library (should be no-op rebuild + passing tests)
- [ ] Make a trivial change, re-run — verify incremental build works
- [ ] Record: download+decompress time, build time, test time

## Phase 3: Skills & Agents

**Goal:** Prompt files in `.agentic/skills/` that define each agent's behavior.

### Copilot writes:
- [ ] `.agentic/skills/select-issues.md` — issue selector prompt (criteria, heuristics, evaluation)
- [ ] `.agentic/skills/fix-issue.md` — fix agent instructions (read issue, find code, implement fix, write tests)
- [ ] `.agentic/skills/review-correctness.md` — correctness review
- [ ] `.agentic/skills/review-breaking.md` — breaking change detection
- [ ] `.agentic/skills/review-style.md` — style/consistency review
- [ ] `.agentic/skills/review-primary.md` — primary reviewer (synthesizes aspect reviews, produces verdict)

### Deferred (not needed for Eon 0):
- [ ] `.agentic/skills/review-security.md`
- [ ] `.agentic/skills/review-perf.md`
- [ ] Performance review agent
- [ ] Issue selector agent / batch orchestrator

## Phase 4: Fix Workflow (agentic)

**Goal:** The main agentic workflow that orchestrates fix → CI → review → tag.

### Copilot writes:
- [ ] `.github/workflows/agentic-fix-issue.md` — the core loop
  - Input: issue number (manual for Eon 0)
  - Creates branch, spawns fix agent, triggers CI, spawns review agents, tags PR
  - Engine: cheap model (Haiku 4.5) for fix, free models for aspect review, Sonnet for primary review
- [ ] Compile: `gh aw compile`

### Validate (PoC 3 — fake issue):
- [ ] Create fake issue in fork (introduce a known bug, file issue describing it)
- [ ] Run `gh aw run agentic-fix-issue` with that issue number
- [ ] Debug: does fix agent find the code? Does CI run? Do reviews fire? Does PR get created?
- [ ] This is where most debugging will happen

## Phase 5: First Real Fix (Eon 0 complete → Eon 1)

- [ ] Pick one real upstream issue (known-easy, ideally one already fixed by a human for comparison)
- [ ] Run the pipeline
- [ ] Analyze: fix quality, review quality, time, cost
- [ ] Write up learnings

## Labels (one-time, can be scripted) — DONE

All 14 `ai:*` labels created in `danmoseley/runtime` via `gh label create` with 2s delay between calls (to avoid rate limiting). Upstream `area-*` labels NOT cloned (`gh label clone` crashed with goroutine error — defer to if/when needed).

```powershell
cd C:\git\runtime

# Clone area-* labels from upstream (preserves names and colors)
gh label clone dotnet/runtime --repo danmoseley/runtime

# Our custom labels (ai:* prefix) — must match agentic.design.md § Label Setup
# Terminal states
gh label create "ai:ready-for-human" --color 0E8A16 --description "PR has passed all automated review gates"
gh label create "ai:failed" --color EEEEEE --description "Agent attempted fix but could not pass review gate"
gh label create "ai:rejected-early" --color EEEEEE --description "Agent determined issue already fixed or not reproducible"
# Confidence
gh label create "ai:high-confidence" --color 0E8A16 --description "All reviews passed, straightforward fix"
gh label create "ai:medium-confidence" --color FBCA04 --description "Minor concerns flagged"
gh label create "ai:low-confidence" --color D93F0B --description "Significant concerns, needs careful review"
# Characteristics
gh label create "ai:quick-review" --color C5DEF5 --description "Small diff, all checks green"
gh label create "ai:needs-domain-expertise" --color D4C5F9 --description "Touches specialized area"
gh label create "ai:longer-review" --color D4C5F9 --description "Larger diff or subtle concerns"
gh label create "ai:has-perf-concern" --color FBCA04 --description "Performance reviewer flagged something"
gh label create "ai:has-breaking-concern" --color D93F0B --description "Breaking change flagged"
gh label create "ai:needs-broader-tests" --color FBCA04 --description "May need tests beyond scoped fork CI"
gh label create "ai:alternative-suggested" --color C5DEF5 --description "Reviewer suggested different approach"
gh label create "ai:security-notice" --color D93F0B --description "Security-sensitive finding"
```

## Infrastructure Branch

All experiment files live on `agentic/infra` branch. Fork `main` is rebuilt as `upstream/main + merge agentic/infra` by the golden build workflow. Commits on `main` only are lost on next golden build.

## Lessons Learned (during setup)

1. **Shallow clones break merge** — `git clone --depth=1` can't find merge base. Use `--filter=blob:none` (blobless clone) instead.
2. **GitHub Actions runners have no git identity** — must `git config user.name/email` before any commit/merge.
3. **`--force-with-lease` is race-prone** — if another PR merges between clone and push, it correctly rejects. Retrigger is fine.
4. **SHA-pin actions** — use commit SHAs not version tags for supply chain security (`actions/checkout@34e11487...`).
5. **Fork only allows squash merge** (inherited from upstream settings) — doesn't affect our git-level operations.
6. **`gh label clone` is buggy** — crashes with goroutine error on large repos. Create labels individually with delay.

## Open Questions to Resolve During Implementation

1. **Disk space for golden build** — `ubuntu-latest` has ~14GB free. Build produces ~11GB artifacts. Very tight. May need a larger runner — check [GitHub's available runner labels](https://docs.github.com/en/actions/using-github-hosted-runners) for the org's configured larger runners (labels vary by org/plan). PoC 1 will answer this.
2. **Agentic workflow dispatching another workflow** — can `agentic-fix-issue.md` trigger `agentic-libs-test.yml` and wait for results? Or does the agentic workflow run CI steps inline? Need to check `gh aw` capabilities.
3. **Context window for fix agent** — how much of the issue + source code + review feedback fits? The cheap model needs a large context window (≥100K tokens). Should be fine for single-library fixes. Monitor during PoC.
4. **PAT scope** — enforce upstream isolation at the token level. Ideal: two fine-grained PATs — one read-only for upstream, one read-write for fork only. A single PAT with write access to both repos undermines the isolation rule. Alternatively, a GitHub App with per-installation permissions achieves the same separation. Verify the exact fine-grained permission set and document it.
5. **Agentic workflow billing in fork** — does using agentic workflows in a personal fork consume the enterprise premium budget, or the user's personal budget? Need to verify.

## Key Decisions Already Made

- Fork: `danmoseley/runtime` (not a new fork)
- Upstream workflows: leave as-is (fork guards make them inert)
- Infra files: `agentic-` prefix, purely additive, never modify upstream files
- Golden build: GitHub Releases, `zstd --ultra -22`, split if >2GB
- Fix branches from fork `main` (not upstream/main) — clean PR diffs
- N issues = N PRs (always, including early rejections via `--allow-empty`)
- Eon model: hard boundaries, force-reset fork main between eons
- Learn & tune between eons, fresh logging per eon

## Files We'll Create

Workflows use the `agentic-` prefix; skill prompts live under `.agentic/skills/`.

```
.github/workflows/
  agentic-golden-build.yml        # Phase 1 - plain Actions
  agentic-libs-test.yml           # Phase 2 - plain Actions
  agentic-fix-issue.md            # Phase 4 - agentic workflow
  agentic-fix-issue.lock.yml      # Phase 4 - compiled from .md

.agentic/skills/
  select-issues.md                # Phase 3 — issue selection criteria
  fix-issue.md                    # Phase 3 — fix agent instructions
  review-correctness.md           # Phase 3
  review-breaking.md              # Phase 3
  review-style.md                 # Phase 3
  review-primary.md               # Phase 3
```
