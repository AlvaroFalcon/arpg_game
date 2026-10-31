---
name: create-pr
description: "Open a pull request for the work on the current branch, with a fixed description: what's new, what changed, what to test and acceptance criteria. Use whenever a PR is to be created in this repo (\"abre el PR\", \"create a PR\", /create-pr). Never pushes to main."
---

# /create-pr

Opens a PR from the current branch into `main` with the project's description format.
**Never push to `main`** (see CLAUDE.md -> "Git rules"): the PR is how work reaches it.

## Steps

1. **Branch check.** `git branch --show-current` must not be `main`. If it is, stop and
   ask the user which branch to use. Commit pending work first (only the files you
   changed — never a moved-aside `global.json`).
2. **Bring main in.** `git fetch origin main`, then `git merge --no-edit origin/main`
   (a merge, never a rebase or force-push). Resolve conflicts; regenerate generated
   files with their tooling.
3. **Prove it.** Build and run the suites the change touches (`scripts/run_all_tests.sh
   [filter]`, the .NET 8 recipe in CLAUDE.md in cloud sessions). Record which suites
   passed and any known flake you re-ran — this goes into the description, honestly.
4. **Push the branch.** `git push -u origin <branch>`.
5. **Read what the PR contains.** `git log --oneline origin/main..HEAD` and
   `git diff --stat origin/main...HEAD`, then read the diff itself. Describe only what
   is in that diff — not earlier, already merged work.
6. **Check for an open PR** for the branch (`mcp__github__list_pull_requests` with
   `head=<owner>:<branch>`, state open). If one exists, update its body instead of
   opening a second one. A merged PR is never reused.
7. **Open the PR** (`mcp__github__create_pull_request`, base `main`) with a short
   imperative title and the body below, in the user's language (Spanish unless they
   write otherwise). Then give the user the full PR link and offer to watch it.

## Description template

```markdown
## Qué hay de nuevo
<!-- What the player or developer gets that didn't exist before. 1–4 bullets, plain words. -->

## Qué ha cambiado
<!-- Behaviour that is different now, grouped by system, naming the main files.
     Include what was removed/disabled and what deliberately stayed. -->

## Qué probar
<!-- Concrete steps the owner can follow in the game: where to go, what to press,
     what they should see. Then the automated tests run, with the result. -->
- [ ] ...

**Tests automáticos:** <suites run and their result; flakes re-run and why>

## Criterios de aceptación
<!-- Checkable statements that must all be true to merge. Observable, not "works well". -->
- [ ] ...

## Notas
<!-- Optional: risks, follow-ups, open questions, design doc updated. -->

🤖 Generated with [Claude Code](https://claude.com/claude-code)
```

Rules for the body:
- Every "Qué probar" step is something a person can do; every acceptance criterion
  is something a person or a test can verify as true/false.
- Report test results faithfully: a failed or skipped suite is stated, never hidden.
- No credentials, tokens, internal hostnames or model identifiers.
- End with the attribution lines the session's system reminder gives, if any.
