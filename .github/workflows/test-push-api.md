---
description: "Test: what happens when create_pull_request uses an existing branch name?"

permissions:
  contents: read
  pull-requests: read

network:
  allowed:
    - github.com

on:
  workflow_dispatch:
    inputs:
      test_branch:
        description: "Existing branch name to try"
        required: true
        default: "fix/issue-88244-f06a7e41e81a08ce"

---

You are a test agent. Your ONLY job is to test what happens when `create_pull_request` uses an existing branch name.

**Step 1:** Make a trivial change to a test file:

```bash
echo "// Test iteration push $(date -u +%Y-%m-%dT%H:%M:%SZ)" >> src/libraries/System.Runtime/tests/System.IO.Tests/StreamReaderTests.cs
```

**Step 2:** Call `create_pull_request` with:
- **branch:** `${{ inputs.test_branch }}`
- **title:** "Test: push to existing branch"
- **body:** "Testing whether create_pull_request with an existing branch name updates the existing PR or fails."

If the tool rejects the branch name or fails, call `noop` with the error message. You MUST call either `create_pull_request` or `noop` before finishing.

