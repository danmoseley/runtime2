# Agentic Bug-Fixing Pipeline — Design Proposal

*Companion to "90% agentic repo" deck. This document is the technical design; the deck is the pitch.*

## Context

CCA, CLI, and similar tools have moved the bottleneck from writing code to **human review**. Triage is also a human bottleneck. We need to increasingly rely on AI for both — while holding the same quality and relevance bars.

All future repo work falls into two buckets:

- **(a) AI-driven, almost autonomous** — issue triage, investigation, bisection, debugging, localized fixes, testable changes, well-specified features. *This is the target of this proposal.*
- **(b) Human/AI collaboration** — major non-mechanical refactors, API design, large changes without clear specs, configuration-specific or hard-to-test work. *Out of scope here.*

MAUI has demonstrated this can work: 120 AI PRs in 2 weeks, using an "inflight branch" with humans reviewing at the aggregation point.

## Goal

Build a **continuous AI workflow** that creates, reviews, and categorizes bug-fix PRs at scale with **human cost driven as close to zero as we can safely reach**. Same quality bar. The point of the first experiment is not to get issues fixed — it's to **tune and learn the agent**. If the agent does a poor job, close all PRs, tune, and start over.

## Design Principles

1. **Human time is the bottleneck, not AI time.** Spend 10 AI hours to save 10 human minutes. Every design decision optimizes for minimizing human attention per fix.
2. **Included-first, escalate on failure.** Start with included (0×) models for fix and review; accept a small steady-state premium cost (~1 request/round from cheap aspect agents). Reserve 1×+ premium escalations for when the included stack fails.
3. **No fix is better than a bad fix.** The pipeline should confidently close issues it can't handle rather than waste human time on poor PRs.
4. **Fork isolation.** All work happens in a personal fork to reduce noise. Upstream only sees polished PRs after full automated vetting. Issue analysis can go in original issues — with caution.
5. **In-repo configuration.** Use skills, scripts — not hidden customization. Anyone can change it.
6. **Feedback loops are first-class.** Every human rejection is an opportunity to improve the process upstream (update review skill, fix prompts, adjust thresholds).

## The Agent Loop

Four phases (high-level pipeline). Humans are not involved until Phase 3. (These are distinct from the implementation "stages" in later sections, which describe per-issue workflow steps.)

### Phase 1 — Issues to PRs

AI investigates issues at scale: bisects, evaluates value and feasibility. Decides which to create a PR for.

### Phase 2 — Reviews

Reviews and tests the fix critically using every resource available — adversarial review, multi-model, multi-aspect. AI time here is cheap; human time is not. **Put in 10 AI hours to save 10 human minutes.**

Includes:

- Any context that might help a reviewer not familiar with the area
- Self-critical analysis of the fix
- Scores/labels for: **confidence in correctness**, **reviewability**, **value of change**, **breakingness**, **"should we fix this?"**
- Where feasible, this prep should reduce the need for a domain expert review — and flag prominently where a domain expert is a must

### Phase 3 — Merging (First Human Touchpoint)

Humans make a pass through approving PRs and merging into the fork. **This is the first point humans are involved.** Quality bar is the same as always — aim is to make it cheaper to hold that bar. Any human feedback is a point to potentially improve the process upstream (e.g., update review skill).

### Phase 4 — Aggregating (out of scope for initial experiment)

Aggregation is not part of the initial agent loop and is handled manually or via existing processes. In a future iteration, a human-triggered workflow may prepare an aggregate PR of merged fixes against upstream, to be committed with rebase, with a high-level second human review and "all the tests" run at this stage, including bisection and diagnosis of any problematic commits. Even in that future state, the upstream PR is always human-initiated — the aggregator prepares the branch, a human creates the PR.

## Architecture Detail

```
┌─────────────────────────────────────────────────────────────────┐
│                        ORCHESTRATOR                             │
│              (runs in fork, included models by default)           │
│                                                                 │
│  1. Issue Selector ──── reads upstream issues                   │
│     + Investigation ──── bisect, repro, value/feasibility       │
│         │                                                       │
│  2. Fix Agent ────────── GPT-4.1 attempts fix (included, 0×)    │
│         │                                                       │
│  3. Review Gate                                                  │
│     a. Aspect Agents (parallel, included + cheap models)         │
│        ├── Breaking change check                                 │
│        ├── Performance review                                    │
│        ├── Style/conventions check                               │
│        ├── Alternative approaches analysis                       │
│        ├── "Should we fix this?" assessment                      │
│        └── Security check                                        │
│     b. Primary Reviewer ── GPT-4.1 synthesizes (0×, included)   │
│         │                                                        │
│    ┌────┴────┐                                                   │
│    │ LGTM?   │                                                   │
│    ├─ Yes ───┤──► proceed to tagging                             │
│    ├─ Minor ─┤──► retry included model with feedback (up to 2 retries) │
│    └─ No ────┘──► escalate: Haiku (0.33×), then Sonnet (1×)      │
│         │                                                        │
│  4. PR Tagger ──── labels & scores for human consumption         │
│         │                                                       │
│  5. Human Queue ── human scans pre-vetted PRs, merge/abandon    │
│         │             (FIRST HUMAN TOUCHPOINT)                  │
│         │                                                       │
│  6. Aggregator ─── (OUT OF SCOPE for this experiment)            │
│                    periodically batches merged fixes into        │
│                    upstream PR (rebase) for second human review  │
└─────────────────────────────────────────────────────────────────┘
```

## Can Included Models Actually Fix Bugs?

**SWE-bench data is encouraging, but we need our own numbers.**

| Model | Class | SWE-bench Verified | Premium multiplier | Notes |
|---|---|---|---|---|
| GPT-4.1 | Standard | ~65-70% | **0** (included) | Strong reasoning, 1M context, zero premium burn |
| GPT-4o | Standard | ~50-60% | **0** (included) | Fast, multimodal, zero premium burn |
| GPT-5 mini | Small/fast | — | **0** (included) | Zero premium burn; coding ability TBD |
| Claude Haiku 4.5 | Small/fast | 73.3% | **0.33** | Nearly matches Sonnet 4 on SWE-bench; ~3 invocations per premium request |
| GPT-5.1-Codex-Mini | Small/fast | — | **0.33** | Cheap Codex variant |
| GPT-5.4 mini | Small/fast | — | **0.33** | Cheap; coding ability TBD |
| Gemini 3 Flash | Small/fast | — | **0.33** | Cheap, weaker reasoning |
| Grok Code Fast 1 | Small/fast | — | **0.25** | Cheapest non-included option |
| Claude Sonnet 4.6 | Standard | ~75% | **1** | Good escalation target |
| GPT-5.1 / GPT-5.2 | Standard | — | **1** | Latest GPT series |
| Claude Opus 4.5 / 4.6 | Premium | ~80%+ | **3** | Expensive — only for human-triggered analysis |

> **Source:** [Requests in GitHub Copilot — Model multipliers](https://docs.github.com/en/copilot/concepts/billing/copilot-requests#model-multipliers) (retrieved 2026-03-27). Multipliers are subject to change; Sonnet 4.6 multiplier explicitly flagged as tentative. The table above is a subset — see the docs page for the full list including all GPT-5.x-Codex variants.

*Approximate SWE-bench Verified numbers above are based on public reports as of early 2025; see [SWE-bench](https://www.swebench.com/) for details and updates. SWE-bench is overwhelmingly Python/TypeScript — we have no direct C# benchmark data. Model selection for this pipeline is based on: (1) SWE-bench as a rough proxy for code-fix ability, (2) multiplier cost, (3) context window size. The PoC will calibrate actual C# performance. Actual model IDs/versions and availability for production use must come from our org's supported Copilot/LLM catalog — these specific model names are illustrative only and should not be hard-coded into code or configuration.*

**Multiplier = premium requests consumed per invocation.** Models with multiplier 0 are "included" in the Copilot paid plan — high-volume use bounded by rate limits, not monthly quota. (GitHub's exact rate-limit policy for included models may evolve; the PoC will surface any practical limits.) 0.33× models cost ~1 premium request per 3 invocations. 1× models burn 1 premium request each. Opus at 3× is expensive for automation.

**Model selection rationale:** We don't have C#-specific benchmarks for most of these models. Our choices are based on:
- **Fix agent default (GPT-4.1):** 0× multiplier, 1M context (can see large files), strong SWE-bench (~65-70%). The only included (0×) model with proven code-fix ability on SWE-bench. GPT-4o is also included but benchmarks lower.
- **Fix agent alternative (Haiku 4.5):** 0.33× (very cheap), surprisingly strong SWE-bench (73.3% — higher than GPT-4.1). Good for the "try a different model family" escalation step.
- **Reviewer / synthesizer (GPT-4.1):** 0× multiplier, 1M context (can ingest all aspect verdicts + full diff). Review quality is harder to benchmark than fix quality — we're betting that a model good at fixing code is also good at spotting problems in fixes. The PoC will test this assumption.
- **Aspect agents (mix of GPT-4.1 and Haiku 4.5):** For focused single-aspect checks (style, security, breaking changes), even smaller models perform well because the task is narrow. We use GPT-4.1 (0×) for the harder aspects (breaking change, performance, security) and Haiku (0.33×) for the easier ones (style, alternatives, "should we fix?").
- **Escalation (Sonnet 4.6, 1×):** Best SWE-bench score (~75%) among 1× models. Only used after the included (0×) and cheap (0.33×) models have failed — the marginal cost is justified by the higher success probability.
- **Opus (3×):** Not in the automated pipeline. Reserved for rare human-triggered deep analysis.

**Caveats on extrapolation:** SWE-bench Verified is overwhelmingly Python repos. dotnet/runtime is C# with generics, spans, unsafe code, a custom build system, and performance-sensitive hot paths. The absolute numbers won't transfer directly. Additionally, SWE-bench measures "tests pass" not "fix is good" — our quality bar is higher than that.

**What we can say:** Included/cheap models demonstrably fix real bugs in real repos at non-trivial rates. The first experiment will calibrate the actual success rate for dotnet/runtime. If included-model first-try success rate is below ~50% for our issue mix, we should flip the default to Sonnet-first for that area (included-first only saves cost when it works more often than not).

**The included-first strategy is sound in principle because:**

- Most community-filed bugs skew easy (null checks, edge cases, off-by-ones, doc fixes)
- The review gate catches failures before any human sees them
- Failed fixes are just closed — no human cost
- **Key metric to track:** included-model first-try success rate, per area. This tells us whether the included-first strategy is actually saving cost or wasting CI time.

## Detailed Pipeline Design

### Upstream Isolation Rule

**The agent MUST NOT modify anything in the upstream repo (dotnet/runtime) without human intervention.** No comments on issues, no labels, no PRs — nothing. All agent activity happens in the fork. The agent reads upstream (issues, code, docs) but never writes to it.

This means:
- Investigation notes, root cause analysis, and diagnosis all go in the **fork PR** (for issues the agent takes on) or the **fork meta issue** (for issues it passes on — see rejection log below).
- If the agent does useful analysis on an issue it rejects (e.g., identifies root cause but the fix is too complex), that analysis is posted to the fork's rejection meta issue — not on the upstream issue. Surfacing that to humans for optional upstream comment is a future enhancement.
- Upstream PRs are created by humans after reviewing the fork PR, not by the agent.

### Stage 0: Sync Fork with Upstream

At the start of each eon, reset the fork's main to upstream and re-apply infra commits:

```bash
git fetch upstream main
git checkout main
git reset --hard upstream/main
# Re-merge the infra branch (our additive .github/ files):
git merge agentic/infra --no-edit
git push origin main --force-with-lease
```

**Important: fork main advances only between eons, not mid-batch.** The eon lifecycle (see below) resets fork main to upstream at the start of each eon, then the golden build pins an immutable baseline. All fix branches within a batch are created from that same pinned fork/main, ensuring deterministic CI against the golden artifacts. Force-push is safe here because nobody else uses the fork, and it guarantees a clean upstream snapshot with only our infra on top.

The branch command is `git checkout -b fix/issue-NNNNN origin/main` — always branching from fork/main (which is synced with upstream at eon start), so that fork PRs show clean diffs (only the code fix, not infra file deletions).

### Stage 1: Issue Selection & Investigation

**Trigger:** `workflow_dispatch` with parameters (initially); graduate to `schedule` (cron) once confident.

**Up-front guidance (human provides once):**

- Type and area of issues — e.g., `area-System.Text.Json` and a few other areas
- Issues that are defects or approved API
- Not too large in scope, testable, clear desired outcome
- Probably limited to ones where tests can run on Linux (agentic workflows are Linux containers only currently)

#### Concrete "Easy Issue" Heuristics (Machine-Checkable)

The issue selector should apply these filters to candidate issues:

| Filter | Rationale |
|--------|-----------|
| Has a code repro or clear steps to reproduce | Agent needs to understand the bug to fix it |
| No unresolved disagreement in thread | Agent reads the full thread and judges consensus (weighting maintainer input over drive-by comments) — comment count alone isn't a useful filter |
| Exactly one `area-*` label | Multiple areas = cross-cutting; zero = untriaged |
| NOT labeled `needs-further-triage`, `question`, `enhancement`, `api-suggestion` | These aren't fixable bugs |
| NOT in complex areas: `area-GC-coreclr`, `area-CodeGen-coreclr`, `area-VM-coreclr`, `area-Interop` | These require deep runtime internals knowledge |
| Filed within last 2 years | Ancient issues may have drifted; code has changed |
| No linked PRs (open or recently closed) | Someone may already be working on it |
| Not assigned to anyone | Respect in-progress work |
| NOT labeled `api-suggestion` without `api-approved` | Unapproved API proposals have unclear success criteria |
| Issue describes concrete expected behavior | Agent needs objective success criteria — "what should happen" |

These are starting heuristics — the experiment will reveal which are too strict or too loose.

#### Escape Hatch: `ai:do-not-attempt` Label

Maintainers can label any upstream issue with `ai:do-not-attempt` to exclude it from the pipeline. This is an **upstream label** — created and managed by dotnet/runtime maintainers, never by the agent. The issue selector reads it but never writes it. If the label doesn't exist in upstream yet, the selector simply has no issues to skip (safe default). The label should be created in dotnet/runtime before enabling the selector at scale.

**What the orchestrator does:**

1. Query upstream issues matching the filter criteria and heuristics above
2. Skip issues with `ai:do-not-attempt`, linked PRs, assignments, or "won't fix" labels
3. Skip issues with extensive discussion suggesting complexity/controversy
4. **Investigate each candidate:**
   - Read the full issue thread
   - Attempt to locate the relevant code and understand the bug
   - Evaluate: feasibility (can AI fix this?), value (should we fix this?), scope (is this contained?), testability, clear success criteria
   - Optionally bisect to find the regression commit
5. Select issues that pass the feasibility/value bar
6. **Log all rejections** with structured reasons (see below)

**Rejection log:** The fork has a pinned **meta issue** (e.g., "Issue Selection Log") where every rejected candidate gets a comment. Each comment includes the upstream issue link, the rejection reason, and any analysis the agent did (root cause, relevant files, sketched approach). This is better than a committed file: searchable in GitHub UI, timestamped, linkable from batch reports, no merge conflicts. The issue selector agent posts to this meta issue as part of its run. The batch report references it for selector tuning.

**Issue selection skill file:** The selection criteria, heuristics, and evaluation guidance live in `.agentic/skills/select-issues.md` — the prompt for the issue selector agent. The eon's human guidance ("focus on System.Text this batch", "avoid perf-related issues", "try some harder ones this time") is injected as dynamic context by the orchestrator. This keeps the stable criteria in the skill file and the per-eon tweaks separate.

**Testability:** The fix must be validatable in our fork CI environment (Linux, standard `ubuntu-latest`). Reject issues that require special configurations (mobile, Windows-only, specific hardware), visual/UI validation, consuming new third-party dependencies we'd need to build, or access to internal infrastructure. The agent doesn't need to enumerate every disqualifier — but if it can't write a test that proves the fix works, it's not a candidate.

**Clear success criteria:** The issue must describe concrete expected behavior — what should happen vs. what happens. Skip issues where there's no agreement on *whether* to fix (unresolved design discussions), *what* the fix should be (competing proposals with no consensus), or where the fix requires an unapproved API change (`api-suggestion` without `api-approved`). If success can't be objectively verified by reading the issue, the agent will waste iterations guessing.

### Stage 2: Fix → Review → Iterate Loop

This is the core of the pipeline. It mirrors what humans do: write a fix, get reviews, address feedback, get re-reviewed, repeat. The key difference is all participants are AI agents and the loop runs autonomously.

**Max iterations:** 5 total across all models (configurable). The initial included model (GPT-4.1, 0×) gets up to 3 attempts; if it fails, Haiku (0.33×) gets 1 attempt; if that also fails, Sonnet (1×) gets 1 final attempt. After 5 total iterations without convergence → abandon with reasons.

```
                    ┌──────────────────────────────┐
                    │                              │
                    ▼                              │
             ┌─────────────┐                       │
             │  Fix Agent   │  (GPT-4.1, included)   │
             │  writes/     │                       │
             │  updates fix │                       │
             └──────┬──────┘                       │
                    │                              │
                    ▼                              │
             ┌─────────────┐                       │
             │  Simple CI   │  (build + unit tests) │
             │  in fork     │                       │
             └──────┬──────┘                       │
                    │                              │
               pass │  fail ──► fix agent retries ─┘
                    │           with error output
                    ▼
             ┌─────────────┐
             │  Review      │  (GPT-4.1, 0×,
             │  Gate        │   multi-aspect)
             └──────┬──────┘
                    │
          ┌─────────┼──────────┐
          │         │          │
        LGTM    Has feedback  Hopeless
          │         │          │
          ▼         │          ▼
       Tag PR       │       Abandon PR
       for human    │       with reasons
                    │
                    └──► fix agent gets
                         review feedback ──────────┘
```

#### 2a. Fix Agent

**Model:** GPT-4.1 (included, 0×) as default. If it fails after retries, escalate to Haiku 4.5 (0.33× — different model family may unstick different problems). Only escalate to Sonnet (1×) as last resort.

**First iteration:**

1. Read the issue in full (description, repro steps, comments)
2. **Already-fixed check:** Many dotnet/runtime issues are silently fixed but not closed. Before attempting a fix, try to reproduce the bug on current `main`. If the repro from the issue no longer reproduces (and the issue describes concrete expected behavior), create an early-rejection PR (empty commit, `ai:rejected-early` label) with the note: "Bug appears already fixed on main — manual verification recommended." This avoids wasting iterations on phantom issues.
3. Create fix branch from fork main: `git checkout -b fix/issue-NNNNN origin/main`
4. Search the codebase for relevant files
5. Read full source files (not just diff hunks)
6. Implement the fix
7. Write or update tests that validate the fix
8. Commit to the fix branch and push to fork

**Subsequent iterations (addressing feedback):**

1. Read the review feedback and/or CI error output from the previous iteration
2. Update the fix to address the specific points raised
3. Commit the update

**Key instruction:** "If you cannot confidently fix this issue, say so clearly and explain why. Do not guess."

**Timeout:** 10 minutes per iteration.

#### 2b. CI in Fork

##### Don't Reuse Upstream CI — Write a Simple Custom Workflow

dotnet/runtime's real CI is Azure Pipelines under `eng/pipelines/` — it's massively complex (multi-OS, multi-arch, Helix test submission, crossgen, coreclr, native builds, etc.). A fork inherits all of this. **Do NOT try to use or adapt it.** Instead, write a small, purpose-built GitHub Actions workflow that does exactly what we need and nothing more.

The fork should have its own `.github/workflows/agentic-libs-test.yml`:

```yaml
name: Library Tests
on:
  workflow_dispatch:
    inputs:
      library:
        description: 'Library name (e.g., System.Text.Json)'
        required: true
      test_project:
        description: 'Test project path (relative to repo root)'
        required: true

jobs:
  test-release:
    runs-on: ubuntu-latest  # 4 CPU / 16 GB RAM / 14 GB SSD (public repos)
    steps:
      - uses: actions/checkout@v4

      - name: Download golden artifacts
        run: |
          gh release download --pattern 'golden-part-*' --dir /tmp
          cat /tmp/golden-part-* | zstd -d | tar xf - -C .
        env:
          GH_TOKEN: ${{ github.token }}

      - name: Build changed library
        run: |
          ./build.sh -subset libs -c Release \
            -projects src/libraries/${{ inputs.library }}/src/*.csproj

      - name: Check formatting
        run: |
          # dotnet format requires the tool to be available — either via tool manifest
          # (dotnet tool restore) or pre-installed on the runner
          dotnet format --verify-no-changes --include "src/libraries/${{ inputs.library }}/**"

      - name: Run tests (Release)
        run: |
          ./build.sh -subset libs.tests -test -c Release \
            -projects ${{ inputs.test_project }}

  test-debug:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Download golden artifacts
        run: |
          gh release download --pattern 'golden-part-*' --dir /tmp
          cat /tmp/golden-part-* | zstd -d | tar xf - -C .
        env:
          GH_TOKEN: ${{ github.token }}

      - name: Build changed library (Debug)
        run: |
          ./build.sh -subset libs -c Debug \
            -projects src/libraries/${{ inputs.library }}/src/*.csproj

      - name: Run tests (Debug)
        run: |
          ./build.sh -subset libs.tests -test -c Debug \
            -projects ${{ inputs.test_project }}
```

This is a sketch — the exact `build.sh` invocations need to be validated in the PoC. The key points: **~50 lines of YAML we fully control**, not 10,000 lines of inherited Azure Pipelines config. The orchestrator workflow triggers this via `workflow_dispatch`, passing the library name and test project path. **Both Release and Debug configurations run in parallel** — some tests are Debug-only (e.g., `Debug.Assert` validation, conditional compilation), so running only Release would miss real failures.

**Handle inherited upstream CI non-invasively** in the fork. Do not delete or modify upstream files like `eng/pipelines/` — keep the experiment infrastructure purely additive. The fork won't have the Azure Pipelines service connection anyway, so inherited Azure pipeline runs will simply not trigger. The GitHub Actions workflows we add are the only required checks for the agentic experiment.

##### Build System Reality

dotnet/runtime does NOT use vanilla `dotnet test`. It uses arcade infrastructure with `build.sh`/`build.cmd`, custom MSBuild props/targets, and specific build subsets. The agent should follow the in-repo build and test instructions (see `docs/workflow/building/libraries/` in dotnet/runtime). The actual commands are more like:

```bash
# Build libs (one-time or after big changes)
./build.sh -subset libs -c Release
# Build and run tests for a specific library
./build.sh -subset libs.tests -test -c Release \
  -projects src/libraries/System.Text.Json/tests/System.Text.Json.Tests/System.Text.Json.Tests.csproj
```

**This must be proven before anything else.** See "Proof-of-Concept" section below.

##### Runner Specifications

**GitHub-hosted runners for public repos (free, unlimited minutes):**

| Label | CPU | RAM | Disk | Notes |
|-------|-----|-----|------|-------|
| `ubuntu-latest` | 4 | 16 GB | 14 GB SSD | Standard — used for per-fix CI |
| `ubuntu-latest` | 4 | 16 GB | 14 GB SSD | Golden build — may be tight on disk |

Larger runners (8+ cores, 32+ GB RAM, 300+ GB disk) exist but are **billed even on public repos** and require Team/Enterprise org setup. We start with standard runners; if disk space is too tight for the golden build (11GB artifacts on 14GB disk), we may need a larger runner.

**Agentic workflow runners** (`.md` files compiled with `gh aw`): these run on `ubuntu-slim` — a lightweight Linux container in the Agent Workflow Firewall sandbox. No SKU choice needed; the agentic engine manages this. The model is specified in the `.md` frontmatter (`engine:` block), not the runner.

##### Build Optimization: Sharing Pre-Built Artifacts Across Fixes

A full `build.sh -subset libs` takes 20-30 minutes and produces ~11GB of artifacts (`bin/` ~5GB, `obj/` ~6GB). For library-level fixes, almost all those binaries can be slightly out of date — you only need to rebuild the one library you changed and its test project. Everything else (coreclr, crossgen, ref assemblies, other libraries) can be shared from a pre-built baseline.

**The key constraint:** each `fix-issue` workflow run gets a fresh GitHub Actions runner. It needs the pre-built artifacts without rebuilding from scratch.

**Approach: Static tarball via GitHub Releases (recommended)**

The simplest, most robust option. A scheduled workflow does a full build, tars the result, and uploads it as a GitHub release asset. Fix workflows download and untar. No dynamic state, no cache invalidation, no self-hosted runner.

```
Golden build workflow (agentic-golden-build.yml):
  Triggers: workflow_dispatch (manual) + schedule (e.g., cron: '0 6 * * 1' = weekly Monday)
  Runs on: ubuntu-latest (one-off, doesn't need to be fast)

  1. Sync fork main to upstream (reset + re-merge infra — matches eon lifecycle):
     git remote add upstream https://github.com/dotnet/runtime.git
     git fetch upstream main
     git checkout main
     git reset --hard upstream/main
     git merge agentic/infra --no-edit
     git push origin main --force-with-lease
  2. Build from the merged main (both configurations for full test coverage):
     ./build.sh -subset clr+libs -c Release                      (~20-30 min)
     ./build.sh -subset libs -c Debug                            (~10-15 min additional)
  3. Compress with zstd (max compression — compress once, decompress many times):
     tar cf - artifacts/ | zstd --ultra -22 -T0 -o golden-artifacts.tar.zst
     (~11GB → estimated ~1.5-2GB; managed DLLs compress very well)
  4. Split for 2GB release-asset limit:
     split -b 1900m golden-artifacts.tar.zst golden-part-
     (`split` is GNU coreutils — standard on every Linux)
  5. Upload new release, then delete previous (this order avoids a gap where
     no release exists — a fix workflow starting mid-update still gets the old one):
     gh release create golden-$(date +%s) golden-part-* \
       --title "Golden build $(date +%Y-%m-%d) @ $(git rev-parse --short HEAD)" \
       --notes "Built from upstream/main $(git rev-parse HEAD)" \
       --latest
     # Do NOT use --prerelease: `gh release download` defaults to latest
     # non-prerelease, so prerelease golden builds silently 404.
     # Delete any older golden-* releases (keep only the one marked latest):
     gh release list --json tagName,isLatest \
       -q '.[] | select(.tagName | startswith("golden-")) | select(.isLatest == false) | .tagName' \
       | xargs -r -I{} gh release delete {} --yes --cleanup-tag

Per-fix CI (each fix-issue run):
  1. Check out fork at fix branch
  2. Download and decompress golden artifacts:
     gh release download --pattern 'golden-part-*' --dir /tmp
     cat /tmp/golden-part-* | zstd -d | tar xf - -C .
     (~30-60 sec: download ~1.5-2GB from GitHub CDN + zstd decompression is very fast)
  3. Rebuild only the changed library:
     ./build.sh -subset libs -c Release -projects src/libraries/System.Foo/src/*.csproj
  4. Run tests:
     ./build.sh -subset libs.tests -test -c Release -projects src/libraries/System.Foo/tests/*.csproj
```

**Why this approach:**
- **Static and deterministic.** The tarball is a snapshot — it doesn't change, expire, or get invalidated. If something breaks, you look at the tarball and the workflow. Nothing else to debug.
- **No self-hosted runner needed.** Runs on standard `ubuntu-latest` GitHub-hosted runners.
- **No storage quota concerns.** Public repos have unlimited release storage on GitHub. The golden-build workflow uploads the new release first, then deletes older `golden-*` releases — so there's never a moment with zero releases available (fix workflows that start mid-update just get the previous one).
- **No dynamic state.** Unlike `actions/cache` (which has eviction, key matching, 10GB limits) or shared filesystems (which have concurrency and staleness issues), a release asset is immutable until explicitly deleted.
- **Compress once, decompress many.** Using `zstd --ultra -22` gives near-maximum compression ratio with blazing-fast decompression (~3-5x faster than gzip). The golden build pays the compression cost once; every fix run benefits from fast decompression. The `split` command (GNU coreutils, standard on all Linux) handles the 2GB-per-asset limit.
- **Robustness over cleverness.** We don't want to debug artifact-sharing infrastructure. This is boring and that's the point.

**Schedule and fork sync.** The golden build is also the natural place to sync the fork's `main` with upstream. Steps: `git fetch upstream main` → `git reset --hard upstream/main` → `git merge agentic/infra` (re-apply our infra) → `git push --force-with-lease` → build → upload. This matches the eon lifecycle reset strategy. Run weekly on a cron schedule, plus manually at the start of each experiment batch. This keeps the fork current and the golden artifacts fresh. Since our infra files are purely additive (new files, never modifying upstream files), the infra re-merge should always be clean.

**The commit hash barely matters.** Since we're only changing library code, not the runtime, a golden build from a week ago works fine. The runtime bits (`testhost/`, `microsoft.netcore.app.ref/`) haven't changed. The library we're fixing will be rebuilt from source anyway. Staleness only matters if upstream makes breaking changes to the build system itself — rare, and detectable (the fix build will just fail).

**Sizing estimate** (to be confirmed in PoC 1):
- `artifacts/` uncompressed: ~11GB
- Compressed (`zstd --ultra -22`): estimated ~1.5-2GB (managed DLLs compress very well with zstd)
- Likely fits in a single release asset (<2GB), or 2 chunks at most
- Download + decompress on GitHub-hosted runner: ~30-60 seconds (GitHub CDN is fast for its own releases, and zstd decompression is near-memcpy speed)

**If we can identify the minimal subset**, it gets even smaller. Library tests primarily need:
- `artifacts/bin/runtime/` (~116MB) — the runtime
- `artifacts/bin/testhost/` (~508MB) — test host for running tests
- `artifacts/bin/microsoft.netcore.app.ref/` (~40MB) — reference assemblies

That's ~700MB uncompressed, maybe ~200-300MB compressed — would download in seconds. But figuring out the exact minimal subset that `build.sh` expects is itself fragile. **Start with the full tarball (robust), optimize later if download time matters.**

**Alternative if self-hosted runner is preferred:**

If download time from releases becomes a bottleneck with high concurrency, a self-hosted Linux runner with the golden build on local disk eliminates the download entirely. But this adds VM maintenance overhead. The release-asset approach should be tried first.

##### Flaky Test Handling

dotnet/runtime has flaky tests — this is a daily operational reality. Before running tests on the fix branch, we need a **baseline**: the golden build should also cache test results from running on `main`. Diff the fix-branch test results against the baseline. If a test fails on both `main` and the fix branch, it's pre-existing — don't count it as a regression. Without this, the fix agent will waste iterations "fixing" tests that were already broken.

##### What the Build Already Validates (For Free)

Building with `build.sh` already runs more than just compilation:

- **Roslyn analyzers** — enabled repo-wide via `eng/CodeAnalysis.src.globalconfig` (source) and `eng/CodeAnalysis.test.globalconfig` (tests). Analyzer packages include `Microsoft.CodeAnalysis.NetAnalyzers`, `Microsoft.CodeAnalysis.CSharp.CodeStyle`, `StyleCop.Analyzers`, and others (see `eng/Analyzers.targets`). These run automatically as part of every build — no extra step.
- **TreatWarningsAsErrors** — most library projects enable this. New warnings introduced by the fix = build failure. The fix agent must produce warning-clean code.
- **ApiCompat** — for libraries with a public API surface, API compatibility checks run as part of the build. Catches accidental public API signature changes.
- **`AnalysisLevel=preview`** — the repo uses preview analysis level, meaning the latest rules are active.

**One cheap addition:** Consider adding a `dotnet format --verify-no-changes` step on just the changed files (~seconds). AI models commonly introduce subtle formatting mistakes (wrong indentation, spacing, trailing whitespace). The style review agent catches these too, but catching them in CI avoids a wasted review iteration.

**Bottom line:** "Build the changed library + run its tests" is already a surprisingly thorough gate — compilation, analyzers, API compat, warnings-as-errors, and unit tests. The only gap is formatting, which is cheap to add.

##### Fix Validation: Regression Test Discipline

CI passing means tests pass — it does NOT mean the fix is correct. A test can be tautological, exercise the wrong code path, or pass regardless of whether the fix is present. To validate the fix actually fixes the bug:

1. **Run the new/modified test on `main` (without the fix).** If it passes, the test doesn't actually test the fix — it's not a regression test. The fix agent should be instructed to rewrite the test.
2. **Run the same test on the fix branch.** If it passes, the fix + test combination is validated.
3. **If the test passes on both branches**, it proves nothing. Feed this back to the fix agent: "your test passes even without your fix — write a test that actually catches the bug."

This is basic TDD discipline and catches a significant failure mode: a confident-looking fix with a token test that CI happily greenlights.

The `agentic-libs-test.yml` workflow should support a `validate_regression` mode that runs the new test against the golden artifacts (main) without the fix, then against the fix branch. Implementation: keep the test changes while temporarily reverting only the product files to `main` (e.g., separate commits for test vs. fix, or `git checkout origin/main -- <product-paths>`), run the test (should fail), then restore the fix version of the product files and run the test again (should pass). A simple `git stash` won't work because it would also remove newly-added test files.

##### CI Output Summarization

CI failures in dotnet/runtime can produce thousands of lines of output. Feeding raw logs to the fix agent will blow its context window. The orchestrator should summarize CI output before passing it to the fix agent:

- **Build failure:** extract error code, file, line, message (e.g., `CS0246: The type or namespace name 'Foo' could not be found in Bar.cs:42`)
- **Test failure:** extract test name, expected vs. actual, stack trace top frame
- **Truncate to ~50 lines max** — enough for the fix agent to diagnose, not enough to overwhelm

##### CI Results

- **Pass:** Proceed to review gate.
- **Fail:** Feed the build output / test failure back to the fix agent for another iteration. The fix agent should be able to diagnose and correct most CI failures (wrong signature, missing using, test assertion needs updating, etc.)
- **Repeated CI failure (2+ times on same error):** Escalate to Haiku (Tier 1, 0.33×). If Haiku also fails on the same CI error, escalate to Sonnet (Tier 2, 1×). If Sonnet also fails, abandon.

**What we intentionally don't run in the fork:** Full runtime CI, coreclr tests, crossgen, other library consumers. That's for the bulk aggregated PR to upstream, which runs the complete CI matrix. The fork CI is a fast-feedback sanity check, not a comprehensive regression suite.

**Consuming-library tests:** Rarely, a fix might affect downstream consumers within the repo (e.g., a change in `System.Runtime` could affect many libraries). The review gate's breaking-change agent should flag this, and the PR summary should note "this may need broader test coverage" for the human. The fix agent does not attempt to run these — that's deferred to the upstream PR.

#### 2c. Review Gate (Multi-Aspect, Funneled Through Primary Reviewer)

##### Architecture: All Agents From Day One, Primary Reviewer Synthesizes

Since aspect agents run on a mix of included (GPT-4.1, 0×) and cheap (Haiku 4.5, 0.33×) models, the total cost of all 6 aspect checks is ~1 premium request per review round (3 Haiku × 0.33). The primary reviewer runs on GPT-4.1 (0×, included). So: **the entire review gate costs ~1 premium request per round** — very cheap. Run all specialized agents from the start to maximize learning data. But to avoid conflicting feedback paralyzing the fix agent, **the primary reviewer is the sole author of feedback to the fix agent** — aspect agents are inputs to the primary reviewer, not direct feedback channels.

```
                    ┌─ Breaking change (GPT-4.1, 0×) ──┐
                    ├─ Performance (GPT-4.1, 0×) ──────┤
  CI passes ───────►├─ Style (Haiku 4.5, 0.33×) ───────┼──► Primary Reviewer ──► Fix Agent
                    ├─ Alternatives (Haiku 4.5, 0.33×) ┤    (GPT-4.1, 0×)       (gets ONE
                    ├─ "Should we fix?" (Haiku, 0.33×) ─┤    Synthesizes all      coherent
                    └─ Security (GPT-4.1, 0×) ─────────┘    verdicts into        instructions)
                          All run in parallel                one feedback
```

**The fix agent never sees raw aspect agent output.** It gets a single, prioritized, non-contradictory set of instructions from the primary reviewer. This prevents the "5 agents pulling in 5 directions" problem.

##### Aspect Agents

All run in parallel on included or cheap models. Each produces a structured verdict: ✅ Pass / ⚠️ Concern (with details) / ❌ Fail (with details).

| Agent | Model | What it checks |
|---|---|---|
| Breaking change | GPT-4.1 (0×) | Public API surface changes, behavioral changes visible to callers |
| Performance | GPT-4.1 (0×) | Hot paths, allocations, boxing, unnecessary copies, lock contention. If the change is in a perf-sensitive area, write an ad-hoc BenchmarkDotNet benchmark (per the `performance-benchmark` skill) or identify an existing benchmark in `dotnet/performance` to run. May invoke `@EgorBot`/MihuBot for automated benchmark execution (suite runs, regex source-gen diffs, JIT diffs, etc.). **Fork support:** pending — asked MihuBot owner about enabling for `danmoseley/runtime`. If unavailable in fork, defer perf benchmarking to the upstream PR stage. Flag `ai:has-perf-concern` and include results rather than blocking the loop. |
| Style/conventions | Haiku 4.5 (0.33×) | Matches patterns in nearby product/test code, repo coding guidelines (CONTRIBUTING.md, coding-style.md, code-review skill), naming, error handling |
| Alternative approaches | Haiku 4.5 (0.33×) | Is there a simpler/better way? Would a maintainer suggest a different approach? |
| "Should we fix this?" | Haiku 4.5 (0.33×) | Is this issue worth fixing? Too much churn? Too risky? |
| Security | GPT-4.1 (0×) | Does the fix introduce a vulnerability? Does the fix *expose* a pre-existing vulnerability? (See security handling below.) |

##### Primary Reviewer (Synthesizer)

**Model:** GPT-4.1 (0× / included — strong reasoning, 1M context handles all aspect verdicts easily)

The primary reviewer receives all aspect verdicts plus the diff and issue context. Its job:

1. **Assess correctness.** Is the fix right? Does it solve the issue? Edge cases?
2. **Synthesize aspect feedback.** Read all verdicts, resolve conflicts, prioritize.
3. **Produce a single coherent response** for the fix agent: what to change and why, in priority order. No contradictions.
4. **Decide the loop action:** all clear → exit loop; actionable feedback → loop back; abandon → close PR.

##### Review Context: What Agents Should Examine

The review agents have the full repo checkout. They should be directed to look at:

1. **Full changed files** — not just diff hunks; surrounding context matters
2. **The upstream issue** — does the fix actually address it?
3. **Direct callers/callees within the library** — will anything break? (grep-based for now; Roslyn LSP in future would be more precise)
4. **Public API surface** — signatures, nullability, behavior contracts
5. **Related tests** — are they sufficient? Do they test the right thing?
6. **Nearby code patterns** — does the fix match the style of surrounding code?

For isolated library fixes, looking beyond the library boundary is rarely needed. Don't load the entire library into context — focus on changed files + direct neighbors.

##### Why Wait for All Aspect Agents?

The primary reviewer needs the full picture to arbitrate conflicts (e.g., perf agent wants one thing, correctness wants another). Partial synthesis would miss the whole point. Since aspect agents run in parallel on included/cheap models, the wall-clock cost is just the slowest agent — typically 1-3 minutes. For the experiment, this is acceptable. If it becomes a bottleneck in production, we could let the primary reviewer start with whatever's available after a timeout and note gaps.

##### Experiment Evolution: Tuning the Review Gate

After each batch, examine the aspect agents' contributions:

1. **Which agents changed outcomes?** If an aspect agent's ⚠️/❌ caused the primary reviewer to loop back or abandon, it earned its keep. If it returned ✅ on every PR, it added no signal — consider dropping it or merging it into the primary reviewer's prompt.

2. **Which agents caused noise?** If the primary reviewer consistently overrode an aspect agent (e.g., "the alternatives agent suggested X but the current approach is fine"), the aspect agent may be too aggressive. Tune its prompt or drop it.

3. **Did the human catch things no agent flagged?** Each miss is a signal — either tune an existing agent's prompt or (in later experiment stages) add a new specialized agent.

4. **Should agents be merged or split?** If breaking-change and security always trigger together, merge them. If perf review needs sub-specialization (allocation vs. lock contention), split it. Let the data decide.

Future experiment stages might also explore:
- Swapping the primary reviewer model (Opus for harder fixes, cheaper model for easy ones)
- Adding a Roslyn LSP for precise caller/callee analysis (currently grep-based)
- Having aspect agents examine each other's output (cross-review)
- Reducing to fewer agents if several consistently add no signal

#### Conflict Resolution Between Reviewers

The specialized agents will sometimes disagree — a performance agent may want one thing while a style agent wants another, or the breaking-change agent may flag something the correctness agent considers essential. Rules:

1. **Hard vetoes.** Security ❌ and breaking-change ❌ are hard vetoes — the fix cannot proceed without addressing them. Breaking change is a softer veto if the upstream issue explicitly called for a breaking change (agent checks for this), in which case it becomes a ⚠️ flag for the human rather than a block.

2. **Primary reviewer arbitrates the rest.** The primary reviewer (GPT-4.1, 0×) sees all verdicts and resolves conflicts. It's explicitly instructed: "When aspect reviewers disagree, explain the tradeoff in your synthesis and make a judgment call. Prefer correctness > security > performance > style > alternatives."

3. **No second-opinion tiebreaker model.** We don't add another model to break ties — the primary reviewer's job *is* synthesis. Adding a tiebreaker adds cost and complexity without clear value. If we find the primary reviewer making poor tradeoff calls, that's a prompt-tuning problem, not an architecture problem.

4. **Unresolvable conflicts → flag for human.** If the primary reviewer can't confidently resolve a conflict (e.g., "perf agent says this allocation matters, I'm not sure"), it tags the PR `ai:needs-domain-expertise` and includes both perspectives in the summary. Let the human decide.

#### Security Handling

The security agent checks two things:

1. **Does the fix introduce a vulnerability?** Standard review — if yes, ❌ veto, fix agent must address it.
2. **Does the fix expose or touch a pre-existing vulnerability already in main?** This is sensitive. If the agent discovers a security issue that exists in the current codebase (not introduced by the fix):
   - Do **not** put exploit details in workflow logs; keep logs high-level and non-actionable (fork Actions logs are public)
   - Tag the PR `ai:security-notice` (deliberately vague label, no technical details)
   - Include in the human-facing summary: "⚠️ Security-sensitive finding — details shared via private security channel; do not discuss publicly"
   - The human follows the repo's responsible disclosure process (typically file a MSRC case or private security advisory) and uses that private channel to obtain full details

#### Why Separate Specialized Reviewers?

It's a fair question whether splitting reviews into separate specialized agents is better than one agent checking everything. The reasoning:

- **Focused attention.** Agents (like humans) do better with a single clear priority than with "check everything." A security-focused agent won't bury a vulnerability finding under style nits. A perf agent won't overlook an allocation because it's distracted by correctness concerns.
- **Cheap parallel execution.** Since aspect agents run on included/cheap models in parallel, the marginal cost of adding more is small (~0.33× each for Haiku agents). It's nearly-free additive signal.
- **Independent redundancy.** Each agent reads the same diff fresh. A concern that one agent misses, another may catch from its specialized perspective.
- **Clear accountability in logs.** When a PR has a perf problem, you can see exactly what the perf agent said vs. didn't say. Easier to tune individual prompts.
- **Tradeoff: duplicated analysis.** Yes, each agent reads the same code. That's intentional — it's the same reason code review works better with multiple reviewers in practice. The cost is context tokens on included/cheap models — roughly ~1 premium request total for the Haiku agents.
- **This is an experiment.** We'll learn whether the specialized agents add real signal or just noise. If a particular aspect agent rarely produces useful feedback, drop it. If two agents consistently overlap, merge them. The architecture makes it easy to add/remove agents.

**The primary reviewer synthesizes all verdicts and decides:**

- **All clear** → Exit loop, proceed to tagging (Stage 3)
- **Actionable feedback** → Write precise instructions for the fix agent, loop back to 2a
- **Escalation: model switch** → If the included model can't get it right after retries, escalation proceeds in two tiers:
  1. **Tier 1 (cheap): Switch model family.** A **new fix agent** is spawned with Haiku 4.5 (0.33×) — a different model family from the default GPT-4.1. Different model families have different strengths — one may see the fix the other missed. It receives: the issue, the diff of the previous model's last attempt, all review feedback so far, and an instruction to "start from the previous attempt if it's salvageable, or start fresh if it's fundamentally wrong." **Budget: 1 iteration.** If it fails, proceed to Tier 2.
  2. **Tier 2 (premium): Sonnet.** A new fix agent is spawned with Sonnet 4.6 (1× multiplier). Same handoff: issue, last diff, all feedback. **Budget: 1 iteration.** If Sonnet also fails, abandon — the issue is genuinely hard or the review gate has a systematic flaw. Don't burn further premium requests on a losing bet.
  
  The escalated model's fix goes through the same review gate (fresh aspect review pass).
- **Abandon** → Issue is too hard, too risky, not worth it, or the fix keeps making things worse. Close the PR with a detailed explanation of why. No human cost.

#### Iteration Mechanics

**How a review round works:**

1. Fix agent commits an update (or initial fix)
2. CI runs (sync — orchestrator waits for pass/fail)
3. If CI passes, orchestrator launches all aspect agents **in parallel** via `task` tool in background mode
4. Orchestrator waits for **all** to return before proceeding
5. Primary reviewer sees all verdicts, synthesizes, decides next action
6. If looping back → fix agent gets precise instructions, goes to step 1

**Why wait for all?** The primary reviewer needs the full picture to arbitrate conflicts (e.g., perf agent wants one thing, correctness wants another). Partial synthesis would miss the whole point.

**Timeout / stuck agent handling:**

- Each aspect agent gets a **5-minute timeout**. If it hasn't returned a verdict, treat it as "⚠️ No response — reviewer timed out." The primary reviewer proceeds without that signal and notes the gap.
- The primary reviewer itself gets a **10-minute timeout**. If it hangs, the orchestrator falls back to a simple rule: if any aspect agent returned ❌, loop back; if all returned ✅, proceed; otherwise flag `ai:medium-confidence` and move to human queue.
- CI gets a **15-minute timeout** (unit tests should be fast). If it times out, treat as a CI failure and retry once; second timeout → abandon.

**What if the fix agent spins?** The fix agent also has a 10-minute timeout per iteration. If it produces no commit, that counts as a failed iteration toward the max-5 limit.

**Net effect:** The entire pipeline for one issue has a hard wall-clock budget of roughly **max_iterations × (10min fix + 15min CI + 10min review) ≈ 175 minutes worst case** for 5 iterations. Happy path (1 iteration, CI passes fast): ~15-20 minutes. **Note:** The 20-minute default agentic workflow agent timeout may require extending, or CI may need to be dispatched as a separate workflow step rather than running inside the agent — the PoC must validate this.

#### Loop Termination

The loop always terminates. Either:

1. **Success:** All reviews pass → tagged for human queue
2. **Abandon (max iterations):** Hit iteration limit → PR closed with a comment summarizing what was tried, what kept failing, and why the agent couldn't converge. This is useful signal — it tells the human "this issue may be harder than it looks" or "the tests need X."
3. **Abandon (hopeless):** Reviewer determines the issue is beyond automated fixing → closed with specific reasons (e.g., "requires understanding concurrency invariants not evident from the code," "fix requires coordinated changes across 4 assemblies")
4. **Abandon (not worth it):** "Should we fix this?" agent or reviewer determines the fix introduces more risk/churn than value → closed with explanation

**All abandoned PRs stay visible in the fork** (closed, not deleted) so the human can spot-check whether abandonment decisions were correct. This is key feedback for tuning.

### Stage 3: PR Classification & Tagging

**Invariant: N issues selected = N PRs created.** Every issue selected at the start of an eon gets a PR, always. This includes issues the agent gave up on, realized were already fixed, or couldn't reproduce. The PR is the universal tracking unit.

- **Succeeded:** PR has a fix, passed review → labeled for human review
- **Failed:** Agent gave up after retries/escalation → PR is opened with an explanation of what went wrong, tagged `ai:failed`
- **Rejected early:** Issue already fixed, can't reproduce, out of scope → empty PR opened with explanation, tagged `ai:rejected-early`

**Empty PRs for early rejections:** GitHub requires at least one commit difference to create a PR. Use `git commit --allow-empty -m "Issue rejected: <reason>"`. The PR has 0 files changed — a nice visual signal that there's nothing to review. The PR body explains why the issue was skipped (already fixed, can't reproduce, out of scope, etc.). The human reads the explanation, optionally closes the upstream issue, then closes the PR.

**Why always a PR?** Completion detection is trivial: count PRs with the eon's prefix. "Select and fix 50 issues" → 50 PRs, guaranteed. The human scans one list, not two (PRs + some other "skipped" log). After human review, every PR is either merged or closed — clean end state.

**Eon completion:** The batch orchestrator knows the eon is done when every dispatched issue has a corresponding PR in a terminal label state (`ai:ready-for-human`, `ai:failed`, or `ai:rejected-early`). It then triggers the batch report.

**Human's dashboard:** `gh pr list --label ai:ready-for-human` is the work queue. `gh pr list --label ai:failed` is the "what went wrong" queue (read for learning, close). `gh pr list --label ai:rejected-early` is "issues to re-triage" (close, maybe close the upstream issue too).

**Required labels (every PR):**

- One of: `ai:ready-for-human`, `ai:failed`, `ai:rejected-early` — **terminal state, exactly one per PR**
- `area-XXXX` — Copied from the upstream issue (e.g., `area-System.Text.Json`). Lets the human sort/filter by area. If the issue has multiple area labels, copy all of them.

**Confidence labels:**

- `ai:high-confidence` — All reviews passed, straightforward fix
- `ai:medium-confidence` — Minor concerns flagged, human should glance at specific points
- `ai:low-confidence` — Significant concerns, needs careful human review

**Characteristic labels:**

- `ai:quick-review` — Small diff, all checks green, human can merge in <30 seconds
- `ai:needs-domain-expertise` — Fix touches area requiring specialized knowledge
- `ai:longer-review` — Larger diff or subtle correctness concerns
- `ai:has-perf-concern` — Performance reviewer flagged something
- `ai:has-breaking-concern` — Breaking change checker flagged something
- `ai:needs-broader-tests` — Fix may affect consuming libraries or areas not covered by the scoped fork CI; human should ensure extra tests run in the upstream PR
- `ai:alternative-suggested` — Review suggested a potentially better approach

**Failed/rejected PRs:**

- `ai:failed` — Agent attempted the fix but couldn't pass review gate after retries + escalation. PR body explains what happened (iterations, errors, review feedback). Left open for human to read and close.
- `ai:rejected-early` — Agent determined before fixing that the issue is already fixed on main, can't reproduce, or is out of scope. PR body explains why. Left open for human to read and close.

Both remain **open** until the human closes them. This keeps the PR list as the single source of truth for eon status.

#### PR Description: No "Fixes #..." Links

**Important:** Fork PRs must NOT include `Fixes #12345` or `Closes #12345` in the PR body. These GitHub keywords would close the upstream issue when the fork PR merges, but the issue should only close when the **aggregate PR to upstream** merges (which is a later, out-of-scope step). Instead, the PR body should use a plain reference: `Related: dotnet/runtime#12345` — this creates a link without triggering auto-close.

### Stage 4: Human Review Queue (First Human Touchpoint)

**What the human sees:** A list of PRs in their fork, pre-sorted by confidence level.

#### PR Summary Comment (Top of Every PR)

The summary is the single most important thing the pipeline produces. It must be **scannable in 10 seconds** — not a wall of text. Format:

```markdown
## 🤖 AI Fix: [one-line description of what was fixed]

**Issue:** dotnet/runtime#12345 — [issue title]
**Approach:** [1-2 sentences on what the fix does and why this approach]
**Iterations:** 1 (first attempt accepted) | **Model:** {{model_used}}

| Aspect | Verdict | Note |
|--------|---------|------|
| Correctness | ✅ | — |
| Security | ✅ | — |
| Breaking change | ✅ | — |
| Performance | ✅ | — |
| Style | ✅ | — |
| Tests | ✅ | Relevant unit tests pass |

**Labels:** `ai:high-confidence` `ai:quick-review`
```

For PRs with concerns, the table makes them instantly visible:

```markdown
| Aspect | Verdict | Note |
|--------|---------|------|
| Correctness | ✅ | — |
| Security | ✅ | — |
| Breaking change | ⚠️ | Adds optional parameter to public method — source compatible, binary compatible |
| Performance | ✅ | — |
| Style | ✅ | — |
| Tests | ⚠️ | Broader tests recommended — System.Net.Http consumes this |
```

**Rules for the summary:**

- **Approach description:** Brief by default. If the fix is a one-liner, one sentence is enough. Only elaborate if the approach involves a tradeoff or non-obvious choice.
- **Verdicts table:** Always present. One row per aspect. ✅/⚠️/❌ with a short note only when there's something to say. Dash for "nothing to report."
- **No agent transcripts in the summary.** The full review discussion goes in a collapsed `<details>` block below, for the rare cases where the human wants to dig in.
- **If applicable:** "⚠️ Broader tests recommended: this fix touches X which is consumed by Y, Z — consider running their test suites in the upstream PR"

#### Full Agent Details (Collapsed)

Below the summary, collapsed sections for the fix agent's reasoning and each reviewer's full report:

```markdown
<details>
<summary>🔧 Fix agent reasoning (click to expand)</summary>

### Root Cause Analysis
[What the agent found — the bug mechanism, relevant code paths, related issues discovered...]

### Approaches Considered
[Different ways to fix it, tradeoffs, why this approach was chosen over alternatives...]

### Investigation Notes
[Bisection results, related issues found, code paths explored, test strategy reasoning...]

</details>

<details>
<summary>🔍 Full review details (click to expand)</summary>

### Correctness Review (GPT-4.1, 0×)
[Full review text...]

### Breaking Change Analysis (GPT-4.1, 0×)
[Full analysis...]

### Performance Review (GPT-4.1, 0×)
[Full analysis...]

[etc.]
</details>
```

The fix agent's section is especially valuable for failed/abandoned PRs — it tells the human what was tried and why it didn't work. For successful PRs, it gives the domain-expert reviewer the full context if they need to dig deeper than the summary.

#### Human Workflow

1. Open fork's PR list, filter by `ai:high-confidence` + `ai:quick-review`
2. Scan summary table → glance at diff → merge or abandon (30 seconds each)
3. Move to `ai:medium-confidence` — read the ⚠️ notes in the table, decide
4. Move to `ai:longer-review` — expand full details where needed
5. Scan `ai:failed` and `ai:rejected-early` — read for learnings, close them all

**Target:** Human processes 10-20 PRs in ~15 minutes for the easy ones, spends real time only on the ones that need it.

## Cost Model (Enterprise, 1,000 premium requests/month)

**Per fix (happy path — included model succeeds, CI passes):**

| Step | Model | Premium Cost |
|---|---|---|
| Orchestrator overhead | (included in workflow) | 0 |
| Fix agent | GPT-4.1 (0×, included) | 0 |
| CI run | N/A (fork Actions) | 0 |
| Primary correctness review | GPT-4.1 (0×) | 0 |
| Aspect checks (×6) | GPT-4.1 (0×) / Haiku 4.5 (0.33×) | ~1 |
| **Total** | | **~1** |

**Per fix (1-2 review iterations before approval):**

| Step | Premium Cost |
|---|---|
| Initial fix (GPT-4.1, 0×) + CI + review | ~1 (aspect Haiku calls) |
| Retry (1-2 additional iterations) + CI + re-review | ~1-2 |
| **Total** | **~2-3** |

**Per fix (escalation — Tier 1: Haiku takes over):**

| Step | Premium Cost |
|---|---|
| Initial GPT-4.1 attempts + reviews | ~2-3 |
| Haiku fix attempt (0.33×) | ~0.33 |
| Review pass for Haiku fix | ~1 |
| **Total** | **~3.5-4.5** |

**Per fix (escalation — Tier 2: Sonnet takes over):**

| Step | Premium Cost |
|---|---|
| All cheaper attempts + reviews | ~3-4 |
| Sonnet fix attempt (1×) | 1 |
| Final review pass (GPT-4.1, 0×) + aspects | ~1 |
| **Total** | **~5-6** |

**Monthly capacity (1,000 premium requests):**

- **Happy path is very cheap but not zero.** The fix agent and primary reviewer use GPT-4.1 (0×, included), but each review round's 3 Haiku aspect agents cost ~1 premium request total (3 × 0.33).
- Assuming 60% first-try (~1 premium) + 20% 1-2 retries (~3) + 10% Tier 1 escalation (~4) + 5% Tier 2 (~6) + 5% abandon (~2): **weighted average ~2 premium requests per fix attempt.**
- At 1,000 premium/month: **~400-500 fix attempts/month** capacity. Comfortable headroom.
- The binding constraint at scale is more likely **GitHub API rate limits** (5,000 requests/hour with PAT) or **concurrent Actions job slots** than premium quota.

**GitHub Actions minutes (separate budget):**

Public repos get **unlimited** Actions minutes (free tier). Private repos get 2,000 min/month. Since the fork is public, Actions minutes are not a cost concern. Release asset downloads from public repos are served via GitHub's CDN and are generally unlimited (the ~100 MB/month transfer limits apply to Git LFS, not release assets). Monitor if operating at scale.

## Concurrency, Queuing, and Observability

### Concurrency Limits and Agentic Workflow Guardrails

GitHub Actions allows up to **20 concurrent jobs** (Enterprise may allow more — confirm for our org). Each `agentic-fix-issue` workflow run consumes one job slot. So for a 50-issue eon:

- **~20 issues process concurrently**, remaining 30 queue and start as slots free up
- Each issue takes ~30-175 minutes (see timing estimates), so a full 50-issue eon may take several hours wall-clock
- This is fine — we're not in a hurry. The agent runs overnight or over a weekend.

**Agentic workflow execution guardrails** (independent of model or subscription tier):
- **1 agent job per engine at a time** within a single workflow run — but multiple workflow runs execute in parallel (each run is its own engine), so this doesn't limit cross-issue parallelism.
- **Default agent timeout: 20 minutes.** This is the agent step timeout, not the workflow timeout. For our fix agent (which includes build + test cycles), we may need to extend this via workflow configuration, or structure the workflow so the agent dispatches CI as a separate step and waits for it. The PoC must validate whether 20 minutes is sufficient for a fix iteration including CI.
- **Read-only agent token** by default; writes go through `safe-outputs`. Our workflow design already accounts for this.
- **Hard delays:** agent assignment (10s), workflow dispatch (5s). Minor overhead per step.

**Model concurrency is not the bottleneck.** Included models (GPT-4.1, GPT-4o) have no premium quota impact — only rate limits apply. Haiku at 0.33× is very cheap. Premium models (Sonnet at 1×, Opus at 3×) have a monthly request cap (1,000) but no per-minute rate limit that matters at our scale. The included-first strategy means most agent work (fix agent, primary reviewer) burns zero premium quota — only Haiku aspect agents and Tier 1-2 escalations hit the cap.

**GitHub API rate limits** are a potential bottleneck at scale:
- **5,000 requests/hour/user** with PAT (REST API)
- **Search API: 30 requests/minute** — this is much lower and applies to searching issues, code, etc.
- At 20 concurrent workflow runs, each making ~50-100 API calls over its ~30-minute lifetime, peak throughput could reach ~1,000-2,000 calls/hour — well within the 5k/hr limit. But if running back-to-back batches or doing heavy issue enumeration, this needs monitoring.
- **Mitigation at scale:** Switch from PAT to GitHub App auth (15,000 requests/hr). Defer this until the PoC reveals whether PAT headroom is sufficient.

### Queuing Strategy

The issue selector dispatches all N issues via `workflow_dispatch`. GitHub Actions queues overflows automatically — jobs wait until a slot opens. No custom queuing needed unless we want priority ordering (e.g., highest-confidence issues first). For now, FIFO is fine.

If we want to control dispatch rate (e.g., start with 10, see how they go, then dispatch more), the orchestrator can do staged dispatch with a configurable batch size.

### Diagnosing Stuck or Slow Agents

With 20+ concurrent workflow runs, we need to spot stuck agents without manually checking each log.

**Built-in signals:**
- **GitHub Actions timeout:** Set `timeout-minutes` on each job (e.g., 180 min). Agents that jam get killed automatically.
- **Workflow run status:** `gh run list --workflow agentic-fix-issue.md` shows all runs with status/duration. Sort by age to find outliers.
- **Annotations:** The agentic workflow can post step-level annotations ("Starting review iteration 3", "CI failed, retrying") that appear in the Actions UI without reading the full log.

**Custom observability (add if needed after PoC):**
- **Progress meta issue:** A second pinned issue in the fork where each workflow posts brief status updates ("Issue #1234: fix attempt 2, CI running"). One comment per issue, edited in-place as status changes. The human can glance at this issue to see the whole eon's progress.
- **Stale detection:** A scheduled workflow that runs every 30 minutes, checks for any `agentic-fix-issue` runs older than 2 hours, and posts a summary to the progress issue.
- **Batch report includes timing:** Wall-clock per issue, time per stage, number of iterations. Identifies patterns (e.g., "issues in area-System.Net consistently take 3× longer").

**For the initial experiment:** `timeout-minutes` + `gh run list` is probably sufficient. We'll be watching closely anyway. Add the progress meta issue if we find ourselves checking individual runs too often.

## Implementation: What Lives Where

Everything is encoded in the fork repo — visible, diffable, tunable between experiment iterations.

### Fork vs Upstream: Infrastructure Isolation

The fork has two kinds of content:
1. **Upstream code** — everything from dotnet/runtime, synced regularly
2. **Experiment infrastructure** — our custom workflows, skills, config (additive files only)

These coexist cleanly because:

- **Fork `main` = upstream/main + our infra commits on top.** Our files are purely additive — new files in `.github/workflows/`, `.github/skills/`, `docs/`. We never modify upstream files.
- **Upstream workflows are already inert in forks.** Almost all have job-level guards (`github.repository == 'dotnet/runtime'` or `github.repository_owner == 'dotnet'`). The few without guards (jit-format, markdownlint) only trigger on paths we won't touch, or need secrets that don't exist in the fork. **No disabling needed.**
- **Fork sync uses reset-and-force-push at eon boundaries** (see eon lifecycle below). The golden-build workflow does `git reset --hard upstream/main` then re-merges the infra branch and force-pushes. Within an eon, fork main is immutable. This is a deliberate choice: force-push is safe because no one else uses the fork, and it guarantees a clean base. The "merge" language elsewhere in this doc refers to re-applying infra commits on top of the fresh upstream snapshot, not a running merge.
- **Fix branches are created from fork `main`** (which is synced with upstream + infra files). This means fork PRs show clean diffs — only the code fix, no infra file noise. For eventual upstream PRs, cherry-pick the fix commit onto a clean branch from `upstream/main`.

```
Fork main branch:
  dotnet/runtime files (synced from upstream)   ← upstream's stuff
  + .github/workflows/agentic-golden-build.yml   ← our stuff (additive)
  + .github/workflows/agentic-libs-test.yml      ← our stuff (additive)
  + .github/workflows/agentic-issue-selector.md  ← our stuff (additive)
  + .github/workflows/agentic-fix-issue.md       ← our stuff (additive)
  + .github/workflows/agentic-batch-report.md    ← our stuff (additive)
  + .agentic/skills/fix-issue.md                 ← our stuff (additive)
  + .agentic/skills/review-primary.md            ← our stuff (additive)
  + .agentic/skills/review-*.md                  ← our stuff (additive)
  + docs/experiment-log.md                      ← our stuff (additive)

Fix branch (e.g., fix/issue-12345):
  created from fork main — includes infra files, plus the code fix
  → fork PR shows ONLY the code fix (clean diff)
  → for upstream PR: cherry-pick fix onto a branch from upstream/main
```

**Upstream workflows are already inert in forks.** Almost all have job-level guards (`github.repository == 'dotnet/runtime'` or `github.repository_owner == 'dotnet'`). The few without explicit guards (jit-format, markdownlint, inter-branch-merge-flow) only trigger on paths/branches we won't touch, or need secrets that don't exist in the fork. The agentic workflow `code-review.lock.yml` requires `COPILOT_PAT_*` secrets that don't exist in the fork, so it will skip. **No disabling needed — verified by reading each workflow's guards.**

### File Layout

```
your-fork/.github/
  workflows/
    # --- Our custom workflows (all prefixed `agentic-`) ---
    agentic-issue-selector.md      # Picks issues from upstream — agentic workflow
    agentic-fix-issue.md           # Per-issue fix→CI→review loop — agentic workflow
    agentic-batch-report.md        # Post-batch analysis — agentic workflow
    agentic-libs-test.yml          # Simple CI: build + test one library — regular workflow
    agentic-golden-build.yml       # Scheduled: full build, upload artifacts — regular workflow
    agentic-labels-setup.yml       # One-time: clone area-* labels from upstream + create ai:* labels

    # --- Upstream workflows (inherited, inert due to fork guards) ---
    backport.yml                   # Has github.repository guard
    code-review.lock.yml           # Needs COPILOT_PAT_* secrets
    jit-format.yml                 # Only triggers on src/coreclr/jit/** paths
    ...etc...

  skills/
    code-review/SKILL.md           # Upstream's review skill — we don't touch this
    ...etc (upstream skills)...

  actions/
    select-copilot-pat/            # PAT pool (if needed later)

your-fork/.agentic/
  skills/
    select-issues.md               # Issue selector prompt — criteria, heuristics, evaluation
    fix-issue.md                   # Fix agent prompt — how to approach a fix,
                                   # what to do with CI failures, when to give up
    review-correctness.md          # Correctness review prompt
    review-breaking.md             # Breaking change detection prompt
    review-style.md                # Style/consistency review prompt
    review-perf.md                 # Performance review prompt
    review-security.md             # Security review prompt
    review-primary.md              # Primary reviewer — synthesizes aspect reviews
  config/                          # Future: model config, thresholds
```

**Why `.agentic/` instead of `.github/skills/`?** Our skill files are plain markdown prompts — the orchestrator workflow reads them with `cat .agentic/skills/review-breaking.md` and passes the content to the agent. They don't need GitHub's auto-discovery mechanism. Keeping them out of `.github/skills/` avoids collision with upstream's real skills (like `code-review/`), prevents them from being auto-discovered by Copilot in the fork, and makes them clearly ours. The dotfile convention (`.agentic/`) also makes them less visible in casual browsing.

### Graduation: Moving to an Official Fork

All our infra files are purely additive — we never modify upstream files. This means:

1. **Identify our files instantly:** `git diff upstream/main...main --diff-filter=A --name-only` lists every file we added. Or just look for the `agentic-` prefix.
2. **Reproduce in a new fork:** check out a fresh fork, copy the listed files. Upstream workflows are already inert in forks (they have `github.repository == 'dotnet/runtime'` guards). Done.
3. **No intermingling risk:** the `agentic-` prefix on all our workflow files makes them visually distinct from upstream's files in `.github/workflows/`. Our agent prompts live in `.agentic/` — completely separate from upstream's `.github/skills/`.

This also helps if upstream adds new workflows — their names won't collide with ours.

### What Format for What

| Thing | Format | Why |
|-------|--------|-----|
| **Issue selector** (query, filter, dispatch) | GitHub agentic workflow (`.md` → `.lock.yml`) | Needs AI to evaluate issue feasibility; dispatches per-issue workflows |
| **Fix loop** (fix, CI, review, iterate) | GitHub agentic workflow (`.md` → `.lock.yml`) | Core AI loop; `safe-outputs` for writes, `task` tool for sub-agents, model selection per agent |
| **Batch report** (analyze, summarize) | GitHub agentic workflow (`.md` → `.lock.yml`) | AI reads all process issues, extracts patterns |
| **CI** (build + test) | Regular GitHub Actions workflow (`.yml`) | No AI needed — just shell commands and caching |
| **Golden build** (scheduled rebuild) | Regular GitHub Actions workflow (`.yml`) | Scheduled shell commands, cache management |
| **Agent instructions** (how to fix, how to review, etc.) | `.agentic/skills/*.md` prompt files | Tunable per-agent prompts, reusable, can be referenced by the orchestrator or by other tools (Copilot CLI, VS Code) |
| **Experiment config** (batch size, model choices, area filters, iteration limits) | YAML frontmatter in workflow `.md` files or a separate `config.yml` | Easy to tweak between experiment iterations, diffable |
| **Learnings / tuning notes** | Markdown in repo (e.g., `docs/experiment-log.md`) | Human-readable record of what changed between iterations and why. Also useful input for the AI retrospective. |

### Key Design Principle: Everything in the Repo

All agent behavior is determined by files in the repo. There are no hidden prompts, no external config, no manual steps. This means:

- **Diffable:** `git diff` between experiment iterations shows exactly what changed
- **Reviewable:** Another human (or AI) can read the skills and understand what the agent is supposed to do
- **Reproducible (mostly):** Check out a commit, run the workflow, get similar behavior. Note: model weights change silently on the provider side — same model ID may behave differently a week later. True bit-for-bit reproducibility of LLM output isn't achievable. What IS reproducible: the infrastructure, prompts, review criteria, and CI gate. Log the exact model IDs used in each run for debugging.
- **Tunable:** Edit a skill prompt, commit, re-run — that's the entire feedback loop

### Experiment Tracking & Process Logs

**The PRs should stay clean for fast human review. Process learning goes elsewhere.**

The purpose of tracking is not to decide about a specific PR — it's to improve the *process* across experiment iterations. Two questions drive the design: (1) What happened during this fix attempt, and why? (2) What patterns can we extract across a batch to tune the pipeline?

#### Per-Fix Tracking: Fork Issues (One Per PR)

For each fix attempt, the **orchestrator** (not the fix agent) opens a companion **issue in the fork** (not upstream). This is a structured process log — the orchestrator observes and records what happened mechanically. The fix agent itself doesn't journal; it stays 100% focused on producing a clean PR for human review.

The orchestrator records observable facts:

- **Issue selection reasoning:** Why this issue was chosen, what signals looked good/bad
- **Model used:** Which model(s) attempted the fix, any escalation events
- **Iteration log:** Each loop iteration — what changed, CI result (pass/fail), each reviewer's verdict, synthesized feedback, what the fix agent did next. These are the orchestrator's observations, not the fix agent's self-commentary.
- **Abandonment reasoning:** If abandoned, the specific trigger (CI loop limit? review rejection? timeout?)
- **Timing:** Wall-clock per stage, total duration, model/token usage
- **Outcome:** `ai:high-confidence` / `ai:needs-human-judgment` / `ai:self-closed`

The issue title follows a convention: `[Process Log] Fix for upstream#NNNNN: <short description>`

Labels on the process issue mirror the PR's outcome: `ai:merged`, `ai:abandoned`, `ai:human-rejected`, etc. This makes bulk querying easy.

The PR links to its process issue in the collapsed details section (not in the summary — the human doesn't need to read it to review the PR).

**Why issues, not log files or PR comments:**
- Issues are **searchable** and **labelable** — crucial for batch analysis
- Issues support **threaded comments** — the orchestrator can append to the issue as it goes, and a human or AI analyst can comment later with observations
- Issues have **metadata** (labels, assignees, milestones) that files don't
- Issues are **visible without cloning the repo** — useful during batch analysis
- Issues **don't clutter the PR** — the PR stays focused on "should this be merged?"
- Issues **survive PR closure** — even after closing all PRs at the end of a batch, the process issues remain for analysis

#### Batch-Level Analysis: Summary Issue + Retrospective

**Learning is retrospective, not real-time.** During a batch, agents fix bugs. After a batch, a separate workflow analyzes what happened.

At the end of each batch, the `agentic-batch-report.md` workflow creates a **batch summary issue** by reading all per-fix process issues and synthesizing patterns. This is the primary input for the human's tuning decisions:

```markdown
## Batch 3 Summary — 20 issues attempted

### Outcomes
| Outcome | Count | Issues |
|---------|-------|--------|
| ai:high-confidence | 12 | #101, #102, ... |
| ai:needs-human-judgment | 3 | #108, #115, #119 |
| ai:self-closed | 5 | #104, #107, ... |

### Patterns Observed
- 4/5 abandoned PRs were in System.Net — issue descriptions too vague for agent
- Style reviewer flagged the same naming pattern 8 times — should update the skill
- Cheap model succeeded on first attempt 60% of the time (target: >50%)
- Average iterations to convergence: 2.3
- Average wall-clock per fix: 8 min

### Quota & Efficiency
- Premium requests used this eon: ~38 of 1,000 monthly budget (3.8%)
- Breakdown: ~35 from Haiku aspect reviews (0.33× × ~105 invocations), 1 Sonnet escalation, ~2 from Haiku fix attempts
- Included-model (0×) invocations: ~80 (GPT-4.1 fix agents + primary reviewers)
- GitHub API calls this batch: ~1,200 total (~60/fix). Peak rate during parallel runs: ~800/hr (well within 5,000/hr PAT limit)
- At this rate: ~400 fixes/month within premium budget
- Projected vs target throughput: 400 projected, 300 target → ✅ on track
- Actions minutes used: 340 min (unlimited for public repos, tracking for awareness)

### Suggested Tuning for Next Batch
- [ ] Exclude System.Net.Http until issue descriptions improve
- [ ] Add naming convention X to style skill
- [ ] Increase iteration timeout from 5min to 7min for aspect reviewers
```

The batch summary is the input to the "Examine results" step of the outer experiment loop. The `agentic-batch-report` workflow reads all per-fix process issues, extracts patterns, and drafts this summary for human review. The human then tunes skills/config and runs the next batch.

**Quota tracking:** The "Quota & Efficiency" section of the batch report is critical for learning whether the pipeline is sustainable at target throughput. Premium request usage can be estimated by counting model calls at each tier — each agentic workflow step logs which model it used. The batch report workflow should sum these across all fix attempts in the batch. With the included-first strategy, premium usage averages ~2 requests/fix (mainly from Haiku aspect agents at 0.33× each), with Sonnet escalations being the main variable cost. Monitor GitHub API call volume as a separate constraint — the 5,000/hr REST limit and 30/min search limit can be the real bottleneck at scale. If API limits are hit, consider switching to GitHub App auth (15,000/hr).

**Who learns, and when:**

| What | Who writes it | When | Purpose |
|------|--------------|------|---------|
| Per-fix process issue | Orchestrator (mechanical) | During each fix attempt | Record observable facts |
| Batch summary issue | `agentic-batch-report` workflow | After batch completes | Synthesize patterns across fixes |
| Tuning decisions | Human | After reading batch summary | Edit skills, adjust heuristics, change models |
| Updated config/skills | Human (or human-directed agent) | Before next batch | Encode learnings into the system |

The fix agent never writes to the process issue — it has no idea the issue exists. The orchestrator wraps around it, observes what happens, and writes structured metadata. This keeps the fix agent's context clean and focused.

#### Raw Logs: GitHub Actions Artifacts

Build output, test results, and full model API transcripts go into **GitHub Actions artifacts** attached to the workflow run. These are ephemeral (expire after 90 days by default) and are only consulted when debugging a specific failure — they're not part of the routine analysis. The process issue should have enough context that you rarely need the raw logs.

#### What Doesn't Go in the Fork Repo as Files

Avoid committing log files, CSVs, or experiment data as files in the repo. They create merge noise, don't benefit from issue metadata, and make the repo history harder to read. The only committed files are **configuration and code** — skills, workflows, scripts, and the design doc.

#### Where Does the Agentic Workflow Configuration Live?

GitHub requires agentic workflows (`.github/workflows/*.md`) to be in the repo they run from. So: **the orchestrator, skills, CI workflow, and all config live in the fork repo.** This is fine — it's the natural home for everything. The fork IS the experiment workspace.

If we wanted a separate "experiment infrastructure" repo (e.g., for tracking across multiple forks or repos), it would only hold documentation and analysis scripts — not the workflows themselves. For now, keep it simple: one fork, everything in it.

### Review & Style Context (How the Review Gate Finds Its Standards)

The review gate and fix agent both need to know the repo's conventions. Sources, in priority order:

1. **Repo code-review skill** (`.github/skills/code-review/SKILL.md`). If present, use it — it's the richest source. dotnet/runtime's is 68KB of real maintainer conventions.
2. **Repo code-review instructions** (`.github/code-review-instructions.md`). Some repos have this instead of or in addition to a skill. dotnet/runtime's just points to the skill, but other repos may have standalone guidance here.
3. **Repo CONTRIBUTING.md, coding-style.md, etc.** Most repos have human-targeted coding and style guidance. The agents should read and adhere to these — they're the authoritative statement of "how we write code here."
4. **Nearby code as style reference.** Perhaps the most important signal: the fix agent should match the patterns, naming, error handling, and test style of the surrounding code in the same directory/namespace. "When in Rome." The style review agent should explicitly check for consistency with neighboring files, not just abstract rules.
5. **arcade-skills and dotnet/skills** for cross-repo conventions that aren't repo-specific.

The orchestrator should discover which of these exist at startup and inject them into the relevant agents' contexts.

### Existing Skills to Leverage

The pipeline should use existing skills rather than reinventing. Priority order: skills in the repo itself, then dotnet/arcade-skills, then dotnet/skills.

**In dotnet/runtime (.github/skills/):**

| Skill | Use in Pipeline |
|---|---|
| **code-review** | Primary review gate. 68KB of rules extracted from 43K+ real maintainer review comments. Use as-is or adapt. |
| **issue-triage** | Issue selection stage. Recommends KEEP/CLOSE/NEEDS INFO after researching duplicates, labels, repros. |
| **ci-analysis** | CI failure diagnosis. Analyzes Azure DevOps and Helix failures to determine if PR is green/blocked/needs investigation. |
| **performance-benchmark** | Performance review aspect. Can generate and run ad-hoc benchmarks to validate impact of a change. |
| **api-proposal** | Breaking change detection angle — understands API proposal process, ref assemblies, approval requirements. |
| **jit-regression-test** | Could help generate regression tests for JIT-area fixes. |
| **add-new-jit-ee-api** | JIT-specific — less relevant for libraries-focused experiment. |
| **vmr-codeflow-status** | Not relevant to this pipeline. |

**In dotnet/arcade-skills (shared across dotnet repos):**

The `dotnet-dnceng` plugin contains several skills directly useful to the fix agent, especially during the CI-failure-diagnosis loop:

| Skill | Use in Pipeline |
|---|---|
| **ci-analysis** | Analyze AzDO/Helix build and test failures. The fix agent invokes this when fork CI fails to diagnose whether the failure is caused by its fix or is a pre-existing flaky test. Includes `Get-CIStatus.ps1` script for structured failure data. |
| **ci-crash-dump** | Download and debug crash dumps from Helix test runs. If a test *crashes* (not just fails), this skill finds the dump, downloads it, and runs `dotnet-dump`/`cdb` to get stack traces. Useful when a fix introduces a native crash. |
| **known-issue-history** | Mine edit history of "Known Build Error" issues to determine whether a failing test is a known flaky test vs. a new regression. Helps the fix agent decide whether to retry or abandon on a CI failure it didn't cause. |
| **flow-analysis** | Dependency flow analysis — less relevant for per-issue fixes but useful for understanding package availability in the golden build. |

**In dotnet/skills (general .NET skills):**

| Plugin | Potential Use |
|---|---|
| **dotnet** | General .NET development guidance |
| **dotnet-test** | Test authoring and execution patterns — useful for the fix agent when writing test cases |
| **dotnet-diag** | Diagnostics — could help with issue investigation and bisection |
| **dotnet-msbuild** | Build system knowledge — useful when CI failures are build-related |
| **dotnet-data** | Data access patterns — relevant if fixing issues in data-related areas |

### Per-Agent Skill Files

Each agent gets its own prompt file — the complete prompt that initializes that agent. These live in `.agentic/skills/` on the `agentic/infra` branch and are the primary tuning surface for the experiment. They're plain markdown — not GitHub-discoverable skills, just text the orchestrator reads and passes to the agent.

```
.agentic/skills/
  select-issues.md            # Issue selector — criteria, heuristics, evaluation guidance
  fix-issue.md                # The fix agent
  review-correctness.md     # Does the fix actually solve the bug?
  review-breaking.md        # Does it break public API/behavior?
  review-style.md           # Coding conventions, naming, patterns
  review-perf.md            # Perf regressions, allocations, hot paths
  review-security.md        # Unsafe code, crypto, input validation
  review-primary.md         # Synthesize aspect reviews into go/no-go
```

**Each prompt file contains:**
1. **Role** — who you are, what you're responsible for
2. **Resources** — specific files to read from the repo (guideline docs, source files, etc.)
3. **Task** — what to produce (a fix, a review verdict, etc.)
4. **Constraints** — what NOT to do, scope boundaries, quality bar

**Dynamic context injected at runtime by the orchestrator:**
- Issue URL and body (fix agent)
- PR URL and diff (review agents)
- CI results (if applicable)
- Aspect review outputs (primary reviewer only)

The skill file is the *static* part; the orchestrator workflow provides the *dynamic* part. This separation means you can iterate on prompts without touching workflow YAML, and vice versa.

### Per-Agent Resource Mapping

Each skill file should reference specific upstream docs. These are files from `dotnet/runtime` (`docs/coding-guidelines/` unless noted) that the agent should `cat` or have summarized:

| Skill File | Key Resources |
|---|---|
| `select-issues.md` | Upstream skill `issue-triage`, `CONTRIBUTING.md` (what kinds of contributions are welcome), area label taxonomy. Dynamic: eon human guidance. |
| `fix-issue.md` | `coding-style.md`, `adding-api-guidelines.md`, `updating-ref-source.md`, `cross-platform-guidelines.md`, `source-generator-guidelines.md`, `CONTRIBUTING.md`, `docs/workflow/building/libraries/` |
| `review-breaking.md` | `breaking-change-definitions.md` (3KB — what counts), `breaking-change-rules.md` (16KB — detailed rules), `breaking-changes.md` (5KB — process/policy), upstream skill `api-proposal` |
| `review-style.md` | `coding-style.md` (8KB), `code-formatting-tools.md` (5KB), `framework-design-guidelines-digest.md` (13KB), upstream skill `code-review` (68KB — real maintainer patterns) |
| `review-perf.md` | `performance-guidelines.md` (2KB), `vectorization-guidelines.md` (60KB — selective; only if SIMD-adjacent), upstream skill `performance-benchmark` |
| `review-correctness.md` | Product source files touched by the fix, test files, nearby API doc comments. No specific guideline doc — this is code-level reasoning. |
| `review-security.md` | No specific guideline doc in-repo. General secure coding patterns + awareness of dotnet-specific concerns (e.g., `Span<T>` safety, `unsafe` blocks, cryptographic API misuse). |
| `review-primary.md` | Aspect review outputs + upstream `code-review` skill. Doesn't need raw guideline docs — it synthesizes. |

**How to provide resources:** The skill file can instruct the agent to `cat` specific paths at the start of its run. For large docs (vectorization at 60KB), use conditional inclusion: "Read `vectorization-guidelines.md` only if the change touches SIMD/Vector types." Context budget matters — don't waste tokens on irrelevant docs.

**Evolving the prompts:** This is the main tuning loop. The batch report tracks which agents flag issues vs. miss them. When a reviewer consistently misses something that a guideline doc covers, add that doc to its skill file. When an agent wastes context on docs it never uses, remove them. Over eons, the skill files converge toward the right resource set and prompt style for each role.

## Feedback Loops

The agent needs to learn from and drive down:

- **Code review iterations** — if the same kind of feedback keeps coming up, encode it in the review skill
- **Fixes rejected** (e.g., too much churn) — update issue selection heuristics
- **Issues rejected** (e.g., too breaking, undesirable) — update feasibility criteria

Continuously encode and update policy and learnings in the skill files and scripts — in-repo, visible to anyone.

## Risks & Mitigations

| Risk | Severity | Mitigation |
|---|---|---|
| **Build system doesn't work in GH Actions container** | Critical | PoC 1 must prove this first. Fallback: self-hosted runner, pre-built Docker image, or review-only pipeline (no CI in loop). |
| **Agentic workflow execution limits too tight** | High | Split orchestrator reduces per-workflow complexity. PoC 3 will reveal actual limits. Fallback: simplify loop (fewer iterations, fewer agents). |
| **Security** — actions triggered by untrusted issue content | Medium | Sandboxed in fork; agentic workflows have safe-outputs; read-only upstream access |
| **API rate limits** | Medium | Batch API calls; cache issue metadata; 5,000 req/hr authenticated is generous for 20-issue batches. Monitor in PoC 2. |
| **Community frustration** — AI posting noise on issues | Low (experiment) | No upstream issue comments for the experiment. Fork-only. |
| **Destabilization** — too many changes too fast | Low (experiment) | Fork isolation; aggregation gated by human; same bar as backporting process |
| **Accountability** — who cleans up, reverses errors, tunes | Low | Defined owner; all config in-repo; abandon rather than merge anything uncertain |
| **Flaky tests waste iterations** | Medium | Golden build baseline comparison; diff-only regression detection |
| **Agent jams / runs forever** | Medium | `timeout-minutes` on all jobs; `gh run list` sorted by age; progress meta issue for at-a-glance monitoring |
| **Concurrent job limit throttles throughput** | Low | 20 concurrent jobs processes 50 issues in ~3 waves. Not a blocker — we're not time-critical. Staged dispatch if needed. |
| **Quality bar skepticism** — reviewers hold AI to higher bar | Expected | This is correct behavior. The quality bar for AI fixes *should* be higher initially. Track and measure. |

## Experiment Methodology

### Core Philosophy: Learn, Not Fix

**The goal of the first experiment is NOT to fix bugs. It is to learn how to build an agent that fixes bugs well.** Actual merged fixes are a side effect, not the objective. This distinction matters because it means:

- Every batch is disposable. Run N issues, examine results, close everything, tune, run again.
- Bad outcomes are good data. A PR that fails badly teaches more than one that succeeds easily.
- The humans reviewing aren't trying to merge — they're evaluating the agent's judgment.

### The Outer Loop (Experiment Iterations)

```
  ┌────────────────────────────────────────────────────┐
  │                                                    │
  ▼                                                    │
 Configure          Run batch           Examine         │
 (prompts,    →     of N issues    →    all results  ──┘
  skills,           (pipeline runs      (human +
  selection         autonomously)        AI analysis)
  criteria)
                         │
                         ▼
                    Close everything.
                    As if it never happened.
                    Tune. Run again.
```

**Each experiment iteration:**

1. **Configure:** Set/update prompts, skills, issue selection criteria, model choices, review thresholds. All changes are committed to the fork repo — visible, diffable, reviewable.

2. **Run a batch:** Pick N issues (start small, e.g., 10-20), run the full pipeline autonomously. No human intervention during the run.

3. **Examine results — with AI help:** After the batch completes, a human + AI retrospective:
   - Which PRs were good? What made them good?
   - Which PRs were bad? Where did the pipeline go wrong — issue selection? fix quality? review missed something? wrong area?
   - Which issues were bad choices to attempt? Why? (Too vague, too cross-cutting, too tied to runtime internals, no clear test, etc.)
   - Which review agents added value? Which were noise?
   - What patterns emerge in the failures?

   **Use AI for this analysis too** — feed it all the PR summaries, review verdicts, and outcomes, and ask it to identify patterns and suggest specific improvements to the prompts/skills/criteria.

4. **Close everything.** Close all fork PRs. Don't merge any of them (yet). The fork is a sandbox — nothing escapes until we're confident.

5. **Tune and repeat.** Update the skills, prompts, and issue selection based on learnings. Go to step 1.

### When to Stop Iterating and Start Merging

Graduate from "learning mode" to "production mode" when:

- Human spot-checks consistently agree with the pipeline's verdicts (>90% of `ai:high-confidence` are genuinely merge-worthy)
- Self-close decisions are consistently correct (spot-check sample)
- Issue selection rarely picks issues that turn out to be bad choices
- The human's reaction to a batch is "yeah, these are fine" not "I need to think about each one"

At that point, start actually merging the good PRs and eventually aggregating to upstream.

### Experimental Parameters (First Batch)

- **Platform:** GitHub agentic workflows (compliance, no PAT issues long-term, full GitHub integration; currently Linux containers only)
- **Isolation:** PRs go into a fork to reduce noise
- **CI:** Simple — relevant library's unit tests only, libraries fixes only
- **Scope:** Narrow issue type and area — e.g., JSON parser and a few other areas; defects or approved API; not too large, testable, clear desired outcome; tests can run on Linux
- **Batch size:** Start with 10-20 issues per experiment iteration. Scale up as confidence grows.
- **Aggregation:** Out of scope for first experiment. If/when we believe the experiment has been successful, a giant aggregated PR is created against upstream (rebase), second person reviews at high level. If tests fail, use AI to bisect. This will be controversial — get high confidence first.

### What We Want to Learn

The experiment is structured to answer specific questions across several dimensions. Each eon's batch report should track progress on these:

**1. Agent Configuration & Model Selection**
- What's the right number of review agents? Do all 6 aspect reviewers add signal, or are some redundant noise?
- Which model routing works best for the fix agent? Is the included-first (GPT-4.1, 0×) + Haiku/Sonnet escalation ladder actually cheaper after accounting for iteration failures, or should we escalate more aggressively?
- Does the primary reviewer (synthesizer) add value over just reading aspect verdicts directly?
- What's the right iteration budget? Is 2 iterations enough, or do some categories of fix need 3?
- How much context does the fix agent actually use? Are we wasting tokens on irrelevant files?

**2. Issue Selection Criteria**
- Which areas/labels have the highest first-try success rate? Which should we avoid?
- What makes an issue "good" for the agent? (Clear repro? Small scope? Existing test infrastructure?)
- What makes an issue "bad"? (Vague description? Cross-cutting concern? Requires runtime internals knowledge? Performance-only?)
- Should we pre-filter by issue age, commenter count, or other proxy for complexity?
- Can the agent reliably detect "already fixed" issues, and how often does that happen?

**3. Review Quality & Calibration**
- How often do review agents correctly veto a bad fix? (True positive rate)
- How often do they incorrectly veto a good fix? (False positive rate — wasted iterations)
- How often do they miss a real problem? (False negative rate — the scary one)
- Does the security reviewer ever find anything? Is it worth the context tokens?
- Are review agent suggestions actionable, or does the fix agent ignore/misinterpret them?

**4. Efficiency & Throughput**
- What fraction of fixes require Sonnet escalation (Tier 2)? Target: <10%.
- What's the actual premium request cost per fix (including failed attempts)? With included-first strategy, target is ~2 premium/fix on average.
- Where are the time bottlenecks? (Golden build? CI? Agent thinking time? 20-min agent timeout? GitHub API rate limits?)
- Does the 20-minute agentic workflow agent timeout suffice for a fix iteration including CI, or must CI be dispatched as a separate workflow step?
- How many GitHub API calls does a typical fix consume? At what scale do we hit the 5,000/hr PAT limit?
- Can we increase throughput by running fix agents in parallel, or do they conflict?

**5. Quality & Human Experience**
- What fraction of `ai:ready-for-human` PRs does the human merge without changes?
- How long does the human actually spend per PR? (Target: <2 min average, <30 sec for high-confidence)
- What kinds of issues does the human catch that the review agents missed?
- Does the PR summary give the human enough information, or do they always dig into the diff?

Track these questions explicitly in batch reports. The "Suggested Tuning" section of each batch report should map findings back to these dimensions.

## Success Metrics

The key goal is: **quality, desirable code changes — only — without regressions, at totally minimal human time.** The experiment succeeds if a human can look at a batch of AI-produced PRs and quickly confirm most of them are good, with very few surprises.

### Primary (must demonstrate)

1. **Quality bar held.** Fixes that reach the human queue are indistinguishable in quality from what a competent human contributor would submit. Measured qualitatively: would a maintainer merge this without hesitation? If the answer is frequently "no" or "I'd want to rewrite this," the experiment hasn't succeeded regardless of volume.

2. **Zero regressions escaped.** No fix that passes through the pipeline introduces a regression — whether caught by CI, review, or post-merge. Even one escaped regression in the first experiment would undermine confidence. This is the hard constraint.

3. **Minimal human time per fix.** A human can process the typical AI-produced PR (review summary → glance at diff → merge/abandon) in under 2 minutes. The high-confidence ones should take under 30 seconds. If the human is spending significant time investigating, understanding, or reworking the fixes, the pipeline isn't doing its job.

### Secondary (track and improve)

1. **Yield rate.** What fraction of issues attempted produce a PR that a human merges? Low yield is OK initially (we'd rather abandon than produce junk), but it should improve over tuning iterations. Starting target: >30% of attempted issues result in merged PRs.

2. **Self-filtering accuracy.** Does the pipeline correctly identify which fixes are good and which aren't? Specifically:
   - PRs tagged `ai:high-confidence` should almost all be merged (>90%)
   - PRs auto-closed should almost all have been correct to close (spot-check sample)
   - Very few "surprises" in either direction (good fixes abandoned, bad fixes reaching human)

3. **Declining human effort over time.** As skills and prompts are tuned based on feedback, human time per batch should decrease. Track: minutes spent per batch, number of PRs requiring more than cursory review, number of feedback items fed back into skills.

4. **Cost efficiency.** Premium requests consumed per successfully-merged fix. With included-model-first strategy, the main premium cost is Haiku aspect reviews (~1 per review round at 0.33× × 3 agents). Track Sonnet escalation rate (target: <10%) and overall premium burn rate. At ~2 premium per fix, 1,000/month supports ~500 fixes — comfortable headroom.

### What "Success" Looks Like (Qualitative)

- A maintainer opens the fork's PR list, sees 50 PRs with labels and summaries
- Spends ~30 minutes scanning them, merges 25-35, closes the rest
- None of the merged ones cause problems downstream
- The closed ones were correctly identified as not-ready or not-worth-it
- The maintainer's main reaction is "this saved me time" not "this created work for me"

### What "Failure" Looks Like

- Most PRs need significant human rework → pipeline not saving time
- Regressions escape → quality bar not held
- Human spends as much time understanding AI output as they'd spend writing the fix → pipeline adds overhead
- Community or team pushback on AI-generated noise → process is net negative

## Diagnosability: Designing for Debugging

This is a complex system. When something goes wrong — and it will — the human shouldn't have to dig through raw workflow logs. The system should be debuggable by an AI assistant (Copilot CLI) reading structured artifacts. Design principle: **I should be able to diagnose any failure by reading the per-fix process issue and at most one workflow run log.**

### Eons: Clear Experiment Boundaries

An **eon** is one complete experiment cycle — from "sync with upstream" through "submit upstream and clean up." No state carries between eons except committed config changes and learnings.

**Full eon lifecycle:**

```
┌─────────────────────────────────────────────────────────────┐
│ EON START                                                   │
│                                                             │
│ 1. Reset fork main                                          │
│    git fetch upstream main                                  │
│    git reset --hard upstream/main                           │
│    git merge agentic/infra  (re-apply our infra files)      │
│    git push origin main --force-with-lease                  │
│                                                             │
│ 2. Apply config/prompt tuning from previous eon's learnings │
│    (edit skill prompts, selector criteria, review prompts, etc.) │
│    Commit to fork main + infra branch                       │
│                                                             │
│ 3. Golden build                                             │
│    Full build on current main → compress → upload release   │
│    This release is IMMUTABLE for the rest of the eon        │
│                                                             │
│ 4. Fix batch                                                │
│    Issue selector picks N candidates                        │
│    Fix agents run (serial or parallel)                      │
│    Each produces a PR in the fork                           │
│                                                             │
│ 5. Human review                                             │
│    Scan PRs, merge good ones into fork main, reject bad     │
│    (Each fix is one squashed commit for easy manipulation)  │
│                                                             │
│ 6. Upstream submission (post-experiment)                    │
│    Create mega PR: fork main → upstream/main                │
│    Fight upstream CI, address reviewer feedback              │
│    Some fixes accepted, some rejected                       │
│                                                             │
│ 7. Cleanup                                                  │
│    Run batch report (analyze what worked, what didn't)      │
│    Log learnings (rejected fixes are high-value signal)     │
│    Delete stale fix branches                                │
│                                                             │
│ EON END → next eon starts at step 1                         │
└─────────────────────────────────────────────────────────────┘
```

**Between eons: Learn & Tune.** This is the human's high-leverage moment. After step 7 (or step 5 in early eons where there's no upstream submission):

1. Read the batch report. What categories of issue succeeded/failed? What did review agents flag that was wrong? What did they miss?
2. Tune: edit skill prompts in `.agentic/skills/`, adjust selector criteria, fix workflow bugs. Commit these to the infra branch.
3. The next eon's step 2 picks up these changes.

**Early eons (experiment phase):** Skip steps 6-7. After human review (step 5), just reset fork main as if the upstream submission happened (because it didn't). The learn & tune phase happens based on the fork PRs alone. The eon boundary looks like:

```
  ... step 5 (human review) ...
  Learn & tune (read batch report, edit prompts)
  Reset fork main to upstream/main (discard all merged fixes)
  Re-merge infra (with tuning changes)
  → next eon
```

**Mature eons:** All steps run. The learn & tune phase gets richer signal from upstream reviewer feedback on the mega PR (step 6).

**Fresh logging per eon:** Each eon starts new process issues in the fork. The batch report for eon N is a standalone artifact — it doesn't append to eon N-1's report. This keeps each eon's data self-contained and makes it easy to compare eons side-by-side. Convention: process issues are titled `[Eon N] fix/issue-XXXXX` and the batch report issue is `[Eon N] Batch Report`.

**What changes between eons:**
- Issue selection criteria (area labels, difficulty, type of bug)
- Prompt tuning (skill file edits based on what went wrong)
- Review gate calibration (what's "high confidence" vs "needs human")
- Pipeline fixes (workflow bugs, CI improvements)
- Upstream main has moved — new golden build picks up latest code

**Planned progression:**
- **Eon 0 (bootstrap):** Manual. Set up fork, infra files, golden build, one fake issue. Validate plumbing.
- **Eon 1:** 3-5 real issues, serialized. Stop after all complete. Analyze, tune, batch report.
- **Eon 2+:** Larger batches, possibly parallel. Increasingly automated. Start upstream submission.

**Within an eon, the golden build is immutable.** Every fix workflow in that eon downloads the same release. No surprise because "someone updated the artifacts mid-batch." If a fix fails, we know it's not because the ground shifted — it's something in the fix, the workflow, or the prompts.

### Structured Stage Logging in Process Issues

Each fix attempt has clear stages. The orchestrator writes a structured comment to the process issue at each stage boundary. This is what I'll read first when debugging:

```markdown
### Stage: Golden Artifact Restore
- Status: ✅ PASSED
- Golden release: golden-1711504800 (built from abc1234)
- Download time: 42s
- Artifacts size: 1.8GB compressed → 11.2GB uncompressed

### Stage: Fix Generation (attempt 1)
- Status: ✅ COMPLETED
- Model: claude-haiku-4.5
- Files changed: src/libraries/System.Foo/src/Bar.cs (+12 -3)
- Duration: 85s

### Stage: CI Build + Test (attempt 1)
- Status: ❌ FAILED
- Workflow run: https://github.com/user/runtime/actions/runs/12345
- Exit code: 1
- Error summary: CS0246: The type or namespace name 'Baz' could not be found
- Duration: 67s
- Action taken: feeding error back to fix agent

### Stage: Fix Generation (attempt 2)
- Status: ✅ COMPLETED
- Model: claude-haiku-4.5
- Files changed: src/libraries/System.Foo/src/Bar.cs (+14 -3)
- Duration: 52s
- Delta from attempt 1: added missing using directive

### Stage: CI Build + Test (attempt 2)
- Status: ✅ PASSED
- Workflow run: https://github.com/user/runtime/actions/runs/12346
- Tests: 142 passed, 0 failed, 2 skipped (pre-existing)
- Duration: 95s

### Stage: Review
- Status: ✅ ALL PASSED
- Correctness: ✅ (claude-sonnet-4.6)
- Breaking change: ✅ (gpt-4.1)
- Style: ✅ (gpt-4.1)
- Performance: ⚠️ minor concern noted (not blocking)
- Security: ✅ (claude-haiku-4.5)
- Duration: 45s total (all agents parallel)

### Stage: PR Created
- PR: #42
- Verdict: ai:high-confidence
- Total wall-clock: 6m 22s
- Total iterations: 2
```

**Why this format matters for debugging:**
- **I can read it via GitHub API** — `gh issue view` or MCP tools give me the full issue body
- **Each stage has a status, duration, and enough context** — I don't need to open the workflow run unless I'm going deeper
- **Workflow run URLs are direct links** — if I need the raw log, one click (or one API call)
- **Error summaries are inline** — the most common debug question ("why did CI fail?") is answered without leaving the issue

### Assertions at Stage Boundaries

Each stage should assert its preconditions before starting. These are cheap checks that catch state corruption early and produce clear errors:

```
Before CI:         assert golden artifacts exist, check total size ≈ expected
Before review:     assert CI passed (don't review code that doesn't build)
Before PR create:  assert at least one review agent passed
Before labeling:   assert PR exists and is open
```

If an assertion fails, the orchestrator writes a clear error to the process issue and stops — no silent fallbacks, no "let's try anyway." **Fail loud, fail early.**

### Naming Conventions for Searchability

Everything is named by upstream issue number, making it trivial to find related artifacts:

| Artifact | Naming pattern |
|----------|---------------|
| Fix branch | `fix/issue-NNNNN` |
| Fork PR | `[AI Fix] <title> (upstream#NNNNN)` |
| Process issue | `[Process Log] upstream#NNNNN: <title>` |
| Process issue label | `eon:1`, `ai:high-confidence` / `ai:self-closed` |
| Workflow run | Triggered with `issue_number` input — visible in run parameters |

Given an upstream issue number, I can find everything: `gh pr list --search "upstream#NNNNN"`, `gh issue list --search "upstream#NNNNN"`, filter workflow runs by input parameter.

### What Makes Debugging Easy for Me (Copilot CLI)

When you say "issue 12345 failed, figure out why," here's what I'll do:

1. `gh issue list --label "eon:1"` → find the process issue for upstream#12345
2. Read the process issue → see which stage failed, with error summary
3. If I need more: follow the workflow run URL → read the log
4. If it's a prompt/skill issue: read the relevant `.agentic/skills/*.md` prompt and the fix agent's output
5. If it's a build issue: read the CI workflow log, check the golden build release

**What helps me:**
- Structured stage comments (I parse these, not prose)
- Direct links to workflow runs (I can read logs via `gh run view --log`)
- Error summaries inline in the process issue (most failures diagnosed without leaving the issue)
- Deterministic naming (I search by issue number, not by guessing)
- Eon labels (I know which golden build was used)

**What hurts me:**
- Prose-heavy logging ("the agent tried several approaches...") — give me structured data
- Missing workflow run links (I can't find the log)
- Silent failures (workflow "succeeded" but did the wrong thing)
- State that only exists in workflow environment variables (gone after the run)

### Debugging Playbook (for Reference)

| Symptom | Where to look first |
|---------|-------------------|
| Fix agent produced wrong code | Process issue → fix generation stage → model used, files changed. Then read the skill prompt and the issue description to check if the prompt was adequate. |
| CI build failed | Process issue → CI stage → error summary. If `CS*` error, fix agent bug. If infrastructure error, check golden artifact integrity. |
| CI tests failed | Process issue → CI stage → workflow run link → test results. Cross-reference with flaky test baseline. |
| Review rejected the fix | Process issue → review stage → which aspect failed. Read the reviewer's concern in the PR. Evaluate if it's valid or a false positive. |
| Workflow itself crashed | GitHub Actions UI → workflow run → find the failed step. Usually a missing permission, bad API call, or timeout. |
| Golden build broken | Check the golden build workflow run. Compare release size to expected. Try downloading and untarring locally. |
| Fix is correct but PR looks wrong | Check the PR template logic in the fix-issue workflow. Read the PR body vs the process issue. |

## Proof-of-Concept: Eon 0 (Bootstrap)

Before investing in the full pipeline, prove things work incrementally. Eon 0 is entirely manual/semi-manual — the goal is to validate infrastructure, not automation.

### Step 0: Fork Setup (manual, one-time)

1. Fork dotnet/runtime
2. Verify upstream workflows are inert (fork guards — already confirmed, no action needed)
3. Commit infra files (all `agentic-*` prefixed) to fork main
4. Verify: `git diff upstream/main...main --diff-filter=A --name-only` shows only our files

### PoC 1: Golden Build + Release

The single biggest implementation risk. Trigger `agentic-golden-build.yml` manually:

1. Does `build.sh -subset clr+libs` complete on `ubuntu-latest`? (disk space? SDK bootstrap?)
2. How long does it take?
3. Does `zstd` compression produce a release asset under 2GB? (or does it need splitting?)
4. Can a second workflow download and untar the release?

**Measure:** Wall-clock for build, compressed size, download+decompress time. Record all numbers.

If this doesn't work (e.g., disk too small for 11GB artifacts on `ubuntu-latest`), we need a self-hosted runner — and the design changes significantly.

### PoC 2: Incremental Build + Test (using golden artifacts)

With the golden release from PoC 1, trigger `agentic-libs-test.yml` manually for a library we HAVEN'T changed (should be a no-op rebuild + passing tests):

1. Download golden artifacts from release
2. "Rebuild" one library (should be nearly instant — nothing changed)
3. Run that library's tests
4. All tests pass?

**Measure:** Download+decompress time, incremental build time, test time. This is the baseline for per-fix CI cost.

Then make a trivial change to one `.cs` file, commit, re-run. Verify incremental build picks up only the changed file.

### PoC 3: End-to-End Single Fake Issue

Create a fake issue in the fork (not upstream). Pick a library, manually introduce a simple bug, file the issue describing it. Then run the full `agentic-fix-issue` workflow for that issue:

1. Issue selection: manual (pass issue number directly)
2. Fix agent attempts it (GPT-4.1, included)
3. CI runs (golden artifacts + incremental build)
4. Review agents run
5. PR created with summary
6. Process issue created with structured stage log

This will reveal all the mechanical problems: context window issues, tool-call limits, prompt gaps, CI dispatch plumbing, review synthesis, structured logging format. **Document everything that breaks in the process issue — that's the test of the logging format too.**

Use a known-answer problem so you can evaluate the fix quality, not just whether the pipeline ran.

### PoC 4: First Real Issue (still serialized)

Pick one real upstream issue (known-easy, ideally one already fixed by a human so you can compare the AI's approach). Run the same pipeline. This tests issue reading from upstream and real-world problem complexity.

## Orchestrator Architecture

### Split Into Composable Workflows

A single monolithic orchestrator that does issue selection → fix → CI → review → tagging is too much logic for one agentic workflow. Agentic workflows are designed for focused tasks (10-30 tool calls), not complex orchestration with dozens of branching steps. Split into:

```
agentic-issue-selector (agentic workflow .md)
  ├─ Runs on schedule or dispatch
  ├─ Queries upstream issues, applies heuristics
  ├─ Outputs: list of issue numbers (JSON artifact or dispatch events)
  └─ Triggers agentic-fix-issue for each selected issue

agentic-fix-issue (agentic workflow .md)
  ├─ Triggered per issue via workflow_dispatch(issue_number)
  ├─ Creates branch, downloads golden artifacts, runs fix→CI→review loop
  ├─ Creates PR with summary, labels
  └─ Creates companion process-log issue in fork (structured stage comments)

agentic-batch-report (agentic workflow .md)
  ├─ Runs after all fix workflows complete (or on schedule)
  ├─ Reads all process-log issues for the eon
  ├─ Generates batch summary issue with patterns and suggested tuning
  └─ Optionally notifies human
```

**Benefits:**
- Each issue has its own workflow run → easier to debug, retry, and monitor
- Natural parallelism: multiple `fix-issue` runs can execute concurrently
- Issue selector can be tested independently of the fix loop
- If one fix crashes, others are unaffected

**How to chain them:** `issue-selector` uses `workflow_dispatch` events or `repository_dispatch` to trigger `fix-issue` for each selected issue. `batch-report` is triggered by a schedule (e.g., run 2 hours after the selector) or manually after inspecting the run.

### Parallel Execution

Sequential processing of 20 issues at 15-175 minutes each = 5-58 hours. Too slow for a learning experiment. Plan:

- **Start with concurrency 3-5** — multiple `fix-issue` workflows running in parallel. Each operates on its own branch, so no git conflicts between concurrent runs.
- **GitHub Actions concurrency limits:** Free tier = 20 concurrent jobs; Enterprise = more. With 3-5 fix workflows each using 1-2 jobs, we're well within limits.
- **Shared resources:** The golden build release is read-only during fix runs (only the scheduled `agentic-golden-build.yml` writes to it), so no contention.
- **Scale up gradually:** If 3-5 concurrent runs work smoothly, increase. If rate limits or runner contention appear, reduce.

### State Management

GitHub agentic workflows are stateless between runs. The orchestrator needs to track state across runs to avoid re-attempting issues, know what's in-flight, etc. **Use GitHub itself as the state store** — no external database needed:

| State | Where it lives |
|-------|---------------|
| **Issue in-flight** | A fork branch `fix/issue-NNNNN` exists → issue is being worked on. Or: a fork PR exists for it. |
| **Issue attempted & abandoned** | Open fork PR with `ai:failed` label. |
| **Issue rejected early** | Open fork PR with `ai:rejected-early` label. |
| **Issue attempted & succeeded** | Open or merged fork PR with `ai:ready-for-human` label. |
| **Iteration count** | Tracked within the `fix-issue` workflow run (in-memory, single run). If the workflow crashes mid-loop, the PR is in an incomplete state — see failure recovery below. |
| **Batch membership** | All PRs/issues from one batch share a label, e.g., `ai:batch-003`. |
| **Which issues not to attempt** | Upstream `ai:do-not-attempt` label, or closed fork PR. |

The issue selector queries fork PRs (open + closed) to build the "already attempted" set. This is a simple GitHub API call, not a custom database. Since all PRs stay open until the human closes them, `gh pr list --state open` gives the complete picture of the current eon.

### Branch Management

- **Naming convention:** `fix/issue-NNNNN` (e.g., `fix/issue-12345`). One branch per issue. Predictable, grep-able.
- **Creation:** Always from the fork's `main` branch (kept in sync with upstream): `git fetch origin main && git checkout -b fix/issue-NNNNN origin/main`
- **Cleanup:** After each batch examination, run a cleanup script: delete branches for closed/abandoned PRs. Or: a scheduled workflow that deletes branches whose PRs are closed and older than 7 days.
- **Scale:** At 50-80 issues/week, branches accumulate fast. The cleanup script is essential — don't rely on manual deletion.
- **Golden build maintenance:** The golden build cache is keyed on `eng/Versions.props` hash. When upstream updates dependencies, the next scheduled golden build picks up the change automatically.

### Label Pre-Creation

The fork needs all labels defined before the first pipeline run. Create a setup script (run once) or a workflow that ensures labels exist:

```bash
# labels.sh — run once to set up the fork
gh label create "ai:ready-for-human" --color 0E8A16 --description "PR has passed all automated review gates"
gh label create "ai:high-confidence" --color 0E8A16 --description "All reviews passed, straightforward fix"
gh label create "ai:medium-confidence" --color FBCA04 --description "Minor concerns flagged"
gh label create "ai:low-confidence" --color D93F0B --description "Significant concerns, needs careful review"
gh label create "ai:quick-review" --color C5DEF5 --description "Small diff, all checks green"
gh label create "ai:needs-domain-expertise" --color D4C5F9 --description "Touches specialized area"
gh label create "ai:longer-review" --color D4C5F9 --description "Larger diff or subtle concerns"
gh label create "ai:has-perf-concern" --color FBCA04 --description "Performance reviewer flagged something"
gh label create "ai:has-breaking-concern" --color D93F0B --description "Breaking change flagged"
gh label create "ai:needs-broader-tests" --color FBCA04 --description "May need tests beyond scoped fork CI"
gh label create "ai:alternative-suggested" --color C5DEF5 --description "Reviewer suggested different approach"
gh label create "ai:failed" --color EEEEEE --description "Agent attempted fix but could not pass review gate"
gh label create "ai:rejected-early" --color EEEEEE --description "Agent determined issue already fixed or not reproducible"
gh label create "ai:security-notice" --color D93F0B --description "Security-sensitive finding"
# Batch labels created dynamically: ai:batch-001, ai:batch-002, etc.
```

Also copy relevant `area-*` labels from upstream (or create them on demand when tagging PRs).

### Failure Recovery

The pipeline runs autonomously — things will break. Design for graceful degradation:

| Failure | What happens | Recovery |
|---------|-------------|----------|
| **Fix agent crashes mid-loop** | PR exists but is incomplete (no review, no labels) | Next run of `fix-issue` can detect an existing PR without `ai:ready-for-human` → resume or abandon. Or: leave it and let batch-report flag "orphaned PRs." |
| **CI timeout / quota exhausted** | Tests didn't run | Retry once. If second attempt fails, mark PR `ai:failed` with reason "CI unavailable." |
| **Review agent hangs** | Timeout fires, primary reviewer proceeds without that signal | Already handled by timeout logic (see Iteration Mechanics). Primary reviewer notes the gap. |
| **GitHub API outage** | Can't create PR, can't read issues, can't label | Workflow fails. Re-run after outage clears. No persistent damage since state is in GitHub (branches, PRs). |
| **Orchestrator (issue-selector) crashes** | Partial list of issues dispatched | Already-dispatched issues proceed normally. Re-run selector to pick up remaining issues (it skips already-in-flight ones). |
| **Agentic workflow tool-call limit hit** | Workflow terminates mid-loop | If a PR exists, it's in an incomplete state → same as "fix agent crashes" above. The key is that incomplete PRs never get `ai:ready-for-human`, so they don't reach the human. |

**Guiding principle:** Incomplete work is never presented to the human. If anything goes wrong, the worst case is a wasted workflow run and an orphaned PR/branch that gets cleaned up in the next batch cycle.

## Open Questions

1. **Agentic workflow execution limits:** The "175 minutes worst case" per issue may exceed GitHub's agentic workflow execution time limit. Also unknown: tool-call limits per workflow, context window per sub-agent. The PoC will reveal these constraints. If limits are too tight for the full loop, the split-orchestrator design helps — each `fix-issue` workflow does less work.

2. **CI failure diagnosis:** Addressed: summarize CI output to ~50 lines (error code + file + line for build failures; test name + expected vs actual + top stack frame for test failures). Exact format needs tuning during PoC — the principle is "enough to diagnose, not enough to overwhelm."

3. **Loop budget:** 5 iterations max feels right, but may need tuning. Too few = abandon fixable issues. Too many = waste time on hopeless cases.

4. **Related work:** Prague team has templated concept based on MAUI bits ([ai-dotnet-bootstrap](https://github.com/kotlarmilos/ai-dotnet-bootstrap/tree/main/assets/templates)). Abhitej has a pluggable ADO triage tool. Should align or share learnings.

5. **Roslyn LSP for review agents:** Currently review agents navigate code via grep (file search). A Roslyn Language Server would give precise "find all callers," "go to implementation," type info — significantly better for checking whether a change breaks callers or misses override sites. This is available experimentally in Copilot but not yet proven reliable. **Phase 2 improvement:** once the pipeline works with grep, swap in LSP and measure review quality improvement.

6. **Custom dashboard / batch view:** If human review throughput becomes the bottleneck, consider a batch view (GitHub Projects board, or `gh pr list --label ai:high-confidence | xargs gh pr merge` one-liner). Don't build upfront — evaluate need after first batches.

## Merge Conflicts & Interacting Fixes

With 50 PRs open against the same repo, two problems emerge:

### 1. Textual Merge Conflicts

Each PR branches from the same `main` at batch start. PRs touching different libraries won't conflict. But if we pick multiple issues in the same area (e.g., 3 bugs in `System.Text.Json`), they'll likely touch overlapping files and conflict when merged sequentially.

**During the experiment (learning phase):** Not an issue — we're not merging, just examining. Each PR is independently correct against `main`.

**When merging to the fork:** As the human merges PRs one by one, later PRs may have conflicts with already-merged ones. Options:

- **Rebase before merge.** After merging PR #1, rebase PR #2 onto the new fork main before merging. This could be automated — the pipeline could have a "pre-merge rebase" step that runs when the human approves a PR. If the rebase fails (conflict), the agent attempts to resolve it; if it can't, it flags `ai:needs-conflict-resolution` for the human.
- **Area-aware batching.** During issue selection, limit to one issue per narrowly-scoped area per batch. E.g., at most one `System.Text.Json` fix per batch. This drastically reduces conflicts at the cost of throughput. Probably too conservative — try the rebase approach first.
- **Sequential merging with auto-rebase.** Merge in order of confidence (highest first). After each merge, auto-rebase remaining PRs. Failures get flagged. This is what a human would do manually — just automate it.

**For the aggregated upstream PR:** This is one big PR (rebase of all merged fixes), so conflicts are between the fixes and whatever else landed on upstream `main` since the batch started. Keeping the fork synced with upstream regularly mitigates this.

### 2. Semantic Interactions (The Harder Problem)

Two fixes can each be correct in isolation but interact badly when combined:

- Fix A adds a null check early in a method; Fix B adds a code path after that point that assumes non-null
- Fix A changes a default value; Fix B's test asserts the old default
- Fix A improves perf by caching something; Fix B's fix invalidates the cache assumption
- Both fixes independently change the same method signature in compatible but different ways

These **won't show up as merge conflicts** — they'll merge cleanly but fail at runtime or in tests.

**Mitigations:**

- **The upstream aggregate CI catches this.** This is the primary safety net. When all fixes are combined and full CI runs, semantic interactions surface as test failures. The AI bisection step can identify which pair of fixes conflict.
- **Same-file awareness at selection time.** If two candidate issues both touch the same file (based on initial investigation), flag them. Don't prevent both from being attempted, but note the potential interaction in both PR summaries so the human knows to be careful about merge order.
- **Post-merge integration test.** After merging N PRs into the fork, run a broader CI pass (not just per-library). This is more expensive but catches interactions before the upstream PR. Could be triggered after every 5-10 merges rather than after every merge.
- **Accept the risk for now.** In the first experiment, we're doing 10-20 issues per batch, likely across different areas. The chance of semantic interaction is low. If it becomes a problem at higher volume, add the integration test step. Don't over-engineer the first iteration.

**Bottom line:** Textual conflicts are mechanical and solvable with auto-rebase. Semantic interactions are the real risk but are low-probability for isolated library fixes and are caught by the upstream aggregate CI. For the experiment phase, this isn't a blocker — just track whether it happens.

---

## Appendix A: Upstream Merge Strategy (Post-Experiment)

*Out of scope for the Stage 1 experiment. Gathering considerations here for when the pipeline matures enough to actually submit fixes upstream.*

### The Flow

1. Fix PRs merge into fork `main` (human approves each one individually)
2. Fork `main` now contains: upstream code + infra files + N merged fixes
3. Periodically, create a "mega PR" from fork to upstream: essentially all the accumulated fixes as one PR (or a curated subset)
4. Upstream reviewers review, some fixes accepted, some rejected
5. Mega PR merges (with only the accepted fixes)
6. **Problem:** fork `main` still contains the rejected fixes

### The Core Problem: Rejected Fix Divergence

After upstream merges the mega PR (minus rejected fixes), fork `main` and upstream `main` diverge:
- Upstream has: original code + accepted fixes
- Fork has: original code + accepted fixes + rejected fixes + infra files

The rejected fixes are now orphaned commits on fork `main` that will never go upstream. They create noise in future diffs and could interfere with future fixes to the same code.

### Option 1: Force-Reset Fork Main (Simplest)

After the upstream mega PR merges:

```
git fetch upstream main
git checkout main
git reset --hard upstream/main
# Re-apply infra commits (from the infra branch or cherry-pick)
git merge agentic/infra  # or cherry-pick the infra commits
git push origin main --force-with-lease
```

**Pros:** Fork `main` is clean. No orphaned commits. Future fix branches start from a pristine base.
**Cons:** Force-push. Any in-flight fix branches based on the old fork `main` need rebasing. Must coordinate: don't force-reset while fix agents are running (i.e., do this between eons).

**This is the recommended approach.** It's the nuclear option but it's simple, predictable, and leaves zero residue. The eon model already provides natural boundaries — force-reset at the start of each eon, right after the golden build syncs with upstream.

### Option 2: Revert Rejected Fixes

For each rejected fix, create a revert commit on fork `main`:

```
git revert <commit-hash-of-rejected-fix>
```

**Pros:** No force-push. History is preserved and auditable.
**Cons:** Revert commits accumulate. If a rejected fix touched the same lines as an accepted one, the revert may conflict. Fork `main` history becomes a mess of fix + revert pairs. And if we later want to re-attempt the rejected fix (improved version), the revert commit can cause confusing merge behavior.

### Option 3: Cherry-Pick Accepted Fixes Only (Instead of Mega PR)

Don't merge fix PRs into fork `main` at all. Keep them as separate branches. When ready for upstream:

1. Create a clean branch from `upstream/main`
2. Cherry-pick only the accepted fixes (one commit per fix)
3. PR that branch to upstream

Fork `main` stays synced with upstream + infra. Fix branches are ephemeral.

**Pros:** Fork `main` never diverges. No cleanup needed. Each fix is an independent cherry-pickable commit.
**Cons:** No integration testing of combined fixes before upstream PR. Harder to do the "merge 10 fixes and run broad CI" step described in the merge conflicts section. Fix branches pile up.

### Option 4: Hybrid — Staging Branch

- Fork `main` stays synced with upstream + infra (never receives fix merges directly)
- A `staging` branch receives merged fixes for integration testing
- When ready for upstream, cherry-pick from staging onto a clean upstream-based branch
- After upstream merge, delete and recreate staging from the new fork `main`

**Pros:** Clean separation. Fork `main` is always pristine. Staging is disposable.
**Cons:** Extra branch to manage. Fix PRs target staging instead of main, which is slightly confusing.

### Recommendation

**Start with Option 1 (force-reset)** — it's the simplest and most robust. The eon model provides natural coordination points. At the start of each eon:

1. Upstream mega PR from previous eon has been merged (or abandoned)
2. Force-reset fork `main` to upstream `main`
3. Re-merge infra branch
4. Golden build runs on fresh main
5. New eon begins with a clean slate

If force-push causes operational problems (e.g., GitHub Actions caches invalidated, PR links broken), consider Option 4 (staging branch) as the next step.

### Mega PR Construction

The mega PR itself needs thought:

- **One commit per fix** (squash each fix PR when merging to fork) — makes cherry-picking and reverting individual fixes easy
- **PR description** should list each included fix with its fork PR number and upstream issue number — reviewers need to know what they're getting
- **CI must pass** on the combined set, not just individually. This is where semantic interactions surface.
- **If upstream rejects individual fixes**, the submitter (human) removes those commits and force-pushes the mega PR branch. The fork cleanup (Option 1) happens separately after merge.
- **Batching cadence:** Weekly? Per-eon? Depends on fix volume and reviewer bandwidth. Start with per-eon (manual, coordinated). If volume increases, consider weekly automated mega-PR creation.

### What Happens to Rejected Fixes?

A rejected fix isn't necessarily a failure — it's data:

- **Upstream reviewer disagreed with the approach:** Log the feedback. The issue may still be valid; the fix needs a different strategy. Re-queue the issue with the reviewer's feedback added to the context.
- **Fix was correct but the issue was invalid/wontfix:** Close the upstream issue. Remove from future candidate pools.
- **Fix introduced regressions caught by upstream CI:** The fork CI was too narrow. Expand CI scope for similar issues in the future. Log what the fork CI missed.
- **Style/approach concerns:** Feed back into the review agent's prompts. "Upstream reviewers prefer X over Y for this kind of change."

All of this feeds the learning loop. Rejected fixes are among the highest-value learning signals.
