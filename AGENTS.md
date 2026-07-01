This project has a web front-end at src/BloomBrowserUI.
The front-end uses pnpm 11.5.2. Never ever use npm or yarn.

# Architecture

- C# backend
- web front-end in React/Typescript
- WebView2 for hosting the web front-end in the desktop app
- We strictly control both ends of the API.
    - Don't worry about legacy API support. If you need to change the API, just change it on both sides.
    - Don't be overly defensive about error handling. If the API is used incorrectly, it's fine for it to throw an error. We want to know about it so we can fix it.

# Code Style

- Always use arrow functions and function components in React
- do not destructure props
- do not define a props data type unless it is huge
- example: export const SomeComponent: React.FunctionComponent<{initiallySelectedGroupIndex: number;}> = (props) => {...}

- Avoid removing existing comments unless your changes make them inaccurate/obsolete
- Avoid adding a comment like "// add this line".

- Style elements using the css macro from @emotion/react directly on the element being styled, using the css prop. E.g. `<div css={css`color:red`}>`

- Where possible style things using @emotion/react rather than using sx objects.

- For Typescript coding style, see ./src/BloomBrowserUI/AGENTS.md

# Testing

- Fail Fast. Don't write code that silently works around failed dependencies. If a dependency is missing we should fail. Javascript itself will fail if we try to use a missing dependency, and that's fine. E.g. if you expect a foo to be defined, don't write "if(foo){}". Just use foo and if it's null, fine, we'll get an error, which is good.
- Try to make it so that test failures indicate what went wrong. For example, `fail("An error occurred in setup; we should not have gotten here")` would be better than `expect(false).toBeTruthy();` and `expect(foo).toBe(3);` would be better than `expect(foo === 3).toBe(true);`.
- Add sanity checks to guard against falsely passing tests. For example, when unit testing a method, sanity check that the test data values are as expected before you call the method, and then after you call the method you can verify that those values have changed as expected.
- When running C# tests with `dotnet test`, never pass `--no-build`. Always let dotnet build the test project first so the tests run against the latest code. A stale DLL can cause tests to pass or fail against an old version of the code, hiding real regressions.


# Terminal
The vscode terminal often loses the first character sent from copilot agents. So if you send "cd" it might just say "bash: d: command not found". Try prefixing commands with a space.

# Running Bloom
- Do not run an already-built `Bloom.exe` directly, because it may be stale and miss local code changes.
- Use a source-aware launcher that picks up the current repo state. Right now the default launcher is `./go.sh` at the repo root.
- Do not launch Bloom with `dotnet run` or `node scripts/watchBloomExe.mjs` unless you are specifically working on the launcher scripts themselves or a better repo-supported source-aware launcher has been documented.

If you create new files for temporary purposes (e.g. output or artifact or log files), be sure to clean them up when you're done and be careful not to accidentally commit them.

# Don't run pnpm build
It is vital that you not run `pnpm build` unless instructed to. If there is already a "--watch" build running, you will wreck it and waste the developer's time. You are welcome to `pnpm lint` if you want to check for errors without building.

# Localization
Whenever you add, modify, or review localizable strings (XLF entries), follow `.github/skills/xlf-strings/SKILL.md`.

The one rule that applies at all times even outside that skill: **only ever edit files under `DistFiles/localization/en/`** — never touch the other language subdirectories.

# Commenting
All public methods should have a comment. So should most private ones!

# Git Committing
Always include a good description when creating a git commit.

# Skills
Reusable, task-specific procedures for this repo live in `.github/skills/<name>/SKILL.md`.
When a request matches one of these, READ the matching `SKILL.md` and follow it as the
authoritative procedure (it may have more files alongside it). These may not be auto-loaded
for non-copilot agents, so you may have to open the file yourself.
