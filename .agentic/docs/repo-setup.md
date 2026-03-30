# Repository Setup for Agentic Pipeline

Settings and configuration needed in a repo to run the agentic bug-fixing pipeline.

## GitHub Settings

### Actions > General
- **Fork pull request workflows from outside collaborators:** "Require approval for first-time contributors who are new to GitHub"
  - The less restrictive option. Bot accounts like `copilot-swe-agent[bot]` are always treated as "first-time contributors" under the stricter setting, requiring manual approval on every PR — even after prior approvals.

### Actions > General > Workflow permissions
- **Read and write permissions** (needed for label management, PR comments)
- **Allow GitHub Actions to create and approve pull requests** (if using auto-merge)

### Copilot > Coding agent
- Enable "Copilot coding agent" for the repository
- Ensure `copilot-setup-steps.yml` is present and working

## Repository Secrets

| Secret | Purpose |
|--------|---------|
| `GH_AW_CI_TRIGGER_TOKEN` | PAT with `repo` scope — used for Copilot assignment (GraphQL), issue creation, and triggering workflows that need elevated permissions |
| `GH_AW_GITHUB_TOKEN` | PAT for GitHub MCP server access in AWF agents |
| `GH_AW_GITHUB_MCP_SERVER_TOKEN` | PAT for MCP search tools |
| `MODELS_TOKEN` | Token for model access (AWF agents) |
| `COPILOT_PAT_0` .. `COPILOT_PAT_9` | Rotating PATs for Copilot agent pools (if using parallel fixes) |

## Required Files

- `.github/workflows/copilot-setup-steps.yml` — Copilot agent environment setup
- `.github/workflows/review-orchestrator.yml` — Main orchestrator
- `.github/workflows/code-review.md` + `.lock.yml` — Code reviewer
- `.github/workflows/api-review.md` + `.lock.yml` — API reviewer
- `.github/workflows/security-review.md` + `.lock.yml` — Security reviewer
- `.github/workflows/review-aggregator.md` + `.lock.yml` — Review aggregator
- `.github/workflows/copilot-dispatch-fixer.yml` — Issue creation + Copilot assignment

## Labels

Create these `ai:` labels (the pipeline will not create them automatically):

```
ai:agent-fix
ai:copilot-fix
ai:high-confidence
ai:medium-confidence
ai:low-confidence
ai:has-breaking-concern
ai:needs-broader-tests
ai:has-perf-concern
ai:security-notice
ai:needs-api-review
ai:ready-for-human
ai:needs-iteration
ai:failed
ai:max-iterations-reached
ai:iteration-1
ai:iteration-2
ai:iteration-3
```

## Quick Test Issues (for workflow testing, not fix quality)

These are trivially fixable issues good for fast E2E loop validation:

| Issue | Description | Notes |
|-------|-------------|-------|
| [dotnet/runtime#105088](https://github.com/dotnet/runtime/issues/105088) | Possible typos in PackedSpanHelpers | ~1 file, spelling fix |
| [dotnet/runtime#118401](https://github.com/dotnet/runtime/issues/118401) | Stale comments in Thread::CLRSetThreadStackGuarantee | ~1 file, comment update |

For harder issues to exercise reviewers, see: #66544 (FAT32 tests), #96143 (iOS socket), #849 (PipeReader AdvanceTo).

## Copilot Agent Bot ID

The Copilot SWE agent bot ID is repo-specific. To find it:

```bash
gh api graphql -f query='{ repository(owner: "OWNER", name: "REPO") { suggestedActors(first: 10, capabilities: [CAN_BE_ASSIGNED]) { nodes { login id } } } }' -H "GraphQL-Features: issues_copilot_assignment_api_support"
```

Look for `copilot-swe-agent` in the results. Update the bot ID in `copilot-dispatch-fixer.yml`.
