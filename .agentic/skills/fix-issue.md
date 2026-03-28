# Fix Agent — Skill Prompt

> **NOTE:** The main workflow prompt (`agentic-fix-issue.md`) already contains all essential guidelines inlined. Do NOT re-read CONTRIBUTING.md, coding-style.md, or review skill files — that wastes turns.

This skill is here as a backup reference only. Key points not in the main prompt:

## dotnet/runtime-Specific Knowledge

- **Nullable annotations:** The repo uses `#nullable enable`. Respect nullability.
- **Spans and memory:** Prefer `Span<T>` / `ReadOnlySpan<T>` over arrays where the codebase already does so.
- **`internal` visibility:** Many types are `internal`. Don't make things `public` just because your test needs access — use `[InternalsVisibleTo]` if already set up, or test via public API.
- **Ref assemblies:** `ref/` directories contain API surface definitions. Don't edit these directly — they're generated from `src/` during the build.
- **Conditional compilation:** `#if DEBUG`, `#if NET`, `#if NETCOREAPP` are common.
- **Test infrastructure:** Tests use `RemoteExecutor` for process isolation, `ConditionalFact`/`ConditionalTheory` for platform-specific tests.

## Handling Review Feedback (Future Iterations)

If the orchestrator re-invokes you with review feedback:
1. Read all feedback before making changes.
2. Address every point — explain if you disagree.
3. Prioritize: Correctness > breaking changes > performance > style.
