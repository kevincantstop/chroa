---
name: rules
description: This skill must be used before changing anything in the Chroa Video Player repository — writing, editing, moving or deleting a file — and again whenever a request reads like permission to change code. It defines the review gate this project runs on: code is proposed in chat and lands only after the user explicitly approves it. Read it before the first file operation of any task.
---

# Project rules

The rules below outrank every other skill and every existing convention in this repository. Where one of
them conflicts with something written elsewhere, or with a precedent from an earlier task, this file wins.

## 1. Land only after an explicit approval

Never write to the repository — create, edit, move, rename or delete a file — until the user has
explicitly approved **that exact code**. Propose it in chat first: a new file in full, a change to an
existing file as precise before/after blocks with the file path. Then wait.

Nothing else counts as permission. Not a detailed request, not a clear goal, not "this is only a
mechanical move", not being asked to start now, not a precedent from an earlier task. **How completely
the user specified the work has no bearing on whether the code has been approved** — those are two
unrelated things, and confusing them is the failure this rule exists to prevent.

## 2. What counts as approval

The user saying, in a message about this code, that it may be written to disk — in their own words, for
example "批准落盘", "可以落盘", "落盘吧", "动手吧", "review 通过". The wording varies; the test is whether
they are authorising this change to be saved.

## 3. What does not count

| Looks like permission | Why it is not |
| --- | --- |
| "Move these files to `X` and rename the namespace" | A task spec. The files, the target and the rename were all named — and it is still not approval of any code. |
| "Start refactoring", "create an attribute in this folder and add the logic" | An imperative describes a need. It does not authorise a diff. |
| "This is fine", "no other problems" | Confirms a *plan* or a description, not writing it to disk. |
| Answering a design question I asked ("use A", "keep the old name") | That is a design decision, not a landing permit. Even if I wrote "say A and I'll land it", A is still not the permit — ask again. |
| Silence, follow-up questions, continued discussion | Not permission. |
| "先落盘我看看效果" | A past habit, not a standing rule. Unless said about the code at hand, propose first. |

## 4. The check before every write

Before **every** `write` or `edit`, answer one question:

> In this turn, or in an earlier message about **this** code, did the user say it may be written?

- No → stop, post the code, wait.
- Yes, but about different code → stop, post this code, wait.
- Unsure → treat it as no.

Being small, mechanical, blocking, or obviously wanted is not an exemption.

## 5. Allowed without approval

- Read-only work: reading files, search, `git status` / `git diff`, checking whether the player is running.
- Build verification: `dotnet build ChroaVideoPlayer.sln -m:1 -p:UsedAvaloniaProducts= --nologo -v:q`.
  Build output (`bin`, `obj`) is not a source change.
- Throwaway probes written to `%TEMP%` or the gitignored `.verify-out/`, deleted afterwards.
- Fixing a compile error **inside** an already-approved change, without widening its scope.
- Memory writes the user asks for in that turn.

**Skill documentation under `.agents/skills/**` is a separate case** — the user opened it up on 2026-10-17:
these files, including new skill folders, are updated without asking. The gate covers the repository's code
and configuration, not the notes that describe it. Keeping them accurate is part of delivering a change, and
each session's findings are folded back into the relevant skill without waiting to be asked.

## 6. Delivering a landed change

Compile success is the deliverable — 0 errors, with the artifacts in place. All UI, visual and
interaction verification belongs to the user; do not write UI-assertion probes, do not paste long tool
output, and keep the report to what changed, what was built, and any decision the user should review.

## 7. If the rule is broken

Do not answer with "I will be careful next time". Name the step that went wrong and which message was
mistaken for permission, state plainly what is now on disk so the user can decide whether to keep it,
then return to the gate: post the next change and wait.

## 8. Renaming a project when only the case changes

`ChroaVideoPlayer.headless` → `ChroaVideoPlayer.Headless` is not a normal rename on Windows, and it breaks
the repository in a way that only shows up on someone else's machine.

- **The filesystem refuses it.** `Rename-Item -Path a -NewName A` fails with "Source and destination path
  must be different", because Windows paths are case-insensitive and both names resolve to the same path.
  Go through an intermediate name:
  `headless` → `chroa-headless-tmp` → `Headless`.
  A locked `obj/` (MSBuild, VBCSCompiler) makes the delete or rename fail too — `dotnet build-server shutdown`
  first.
- **Git does not see it.** With `core.ignorecase = true` (the Windows default) `git status` reports nothing
  and the index keeps the old case. Committing then leaves `HEAD` self-contradictory: the `.sln` references
  `ChroaVideoPlayer.Headless\…csproj` while the tree contains `ChroaVideoPlayer.headless/`. It builds on
  this machine and **fails on any case-sensitive checkout**. Record it explicitly:

  ```powershell
  git rm -r --cached "ChroaVideoPlayer.old"
  git add "ChroaVideoPlayer.New"
  ```

  Afterwards `git status` shows pure renames with 0 content changes.
- **Check before committing** that the references and the tree agree:

  ```powershell
  git show HEAD:ChroaVideoPlayer.sln | Select-String -Pattern 'eadless'
  git ls-tree --name-only HEAD | Select-String -Pattern 'eadless'
  ```

