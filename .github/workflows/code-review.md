---
description: "Review pull request changes for correctness, performance, and consistency with project conventions"

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
  fetch-depth: 50

safe-outputs:
  add-comment:
    max: 1
    target: "triggering"
    hide-older-comments: true
    discussions: false
    issues: false

on:
  pull_request:
    types: [opened, synchronize]

engine:
  id: copilot
  model: claude-opus-4.6
---

# Code Review

You are an expert code reviewer for the dotnet/runtime repository. Your job is to review pull request #${{ github.event.pull_request.number }} and post a thorough analysis as a comment.

## Step 1: Load Review Guidelines

Read the file `.github/skills/code-review/SKILL.md` from the repository. This contains the comprehensive code review process, analysis categories, output format, and verdict rules for dotnet/runtime.

## Step 2: Review and Post

Follow the instructions in SKILL.md to perform a thorough code review of PR #${{ github.event.pull_request.number }}. When completed, post the review output as a regular comment on the PR using the `add-comment` safe output.
