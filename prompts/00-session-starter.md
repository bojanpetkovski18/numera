# Session Starter (paste this at the start of every coding session)

Copy everything below the line into Claude Code (or any AI coding agent), replacing `<MILESTONE FILE>` with the milestone you are working on, for example `prompts/M04-import-upload-and-validation.md`.

---

You are my coding agent on the Numera project. I am a professional full-stack developer (Angular, .NET, EF Core, DDD/CQRS) supervising you, so explain important decisions briefly and do not over-explain basics.

Before doing anything:
1. Read `CLAUDE.md`, `docs/SPEC.md`, `PROGRESS.md` and `DECISIONS.md`.
2. Read `<MILESTONE FILE>` completely.
3. Inspect the current state of the repository (file tree, `git status`, `git log --oneline -10`). Do not assume anything exists that you have not seen.

Then:
4. Summarize in 5 lines or fewer: what the milestone requires, what already exists, and your implementation plan as a short numbered list of small steps. Wait for my "go" only if something is ambiguous; otherwise start.
5. Implement **only this milestone**, one numbered step at a time. After each step, run the relevant build and tests and look at the actual output before moving on.
6. Write the tests the milestone requires. Do not delete, skip or weaken a test to make it pass.
7. Start the app and exercise the feature for real where the milestone says to, and report what you saw.
8. Update `PROGRESS.md` and `DECISIONS.md`.
9. Finish with the milestone report in the exact format defined in `CLAUDE.md`.

Hard rules:
- Never claim something compiles, passes, runs or is secure unless you ran it in this session and saw the result. If you could not run it, say so.
- Do not start the next milestone, add unrequested features, or refactor unrelated code.
- Do not delete files or run destructive commands (`rm -rf`, `git reset --hard`, dropping databases other than the local dev one) without asking me first.
- No secrets in the repo. Fictional data only. No employer code or rules.
- If you are blocked or the spec conflicts with the milestone, stop and ask one clear question.

After your report, I will review the diff myself before we continue.
