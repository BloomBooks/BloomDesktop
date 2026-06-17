This project has a web front-end at src/BloomBrowserUI.
The front-end uses yarn 1.22.22. Never ever use npm.

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


# Terminal
The vscode terminal often loses the first character sent from copilot agents. So if you send "cd" it might just say "bash: d: command not found". Try prefixing commands with a space.

# Running Bloom
- Do not run an already-built `Bloom.exe` directly, because it may be stale and miss local code changes.
- Use a source-aware launcher that picks up the current repo state. Right now the default launcher is `./go.sh` at the repo root. If a build fails with errors like missing `PodcastUtilities`, `IDevice`, or other types/namespaces
that "could not be found" (CS0246) in files such as `src/BloomExe/Publish/BloomPub/usb/AndroidDeviceUsbConnection.cs`, the problem is probably that this worktree has not got its dependencies yet. Fix that with `./init.sh`.

- Do not launch Bloom with `dotnet run` or `node scripts/watchBloomExe.mjs` unless you are specifically working on the launcher scripts themselves or a better repo-supported source-aware launcher has been documented.

If you create new files for temporary purposes (e.g. output or artifact or log files), be sure to clean them up when you're done and be careful not to accidentally commit them.

# Don't run build
It is vital that you not run `yarn build` unless instructed to. If there is already a "--watch" build running, you will wreck it and waste the developer's time. You are welcome to `yarn lint` if you want to check for errors without building.

# Localization
- Localizations for translatable strings are kept in DistFiles/localizations; new ones are initially added to one of the files in the "en" subdirectory: There are high (DistFiles/localization/en/Bloom.xlf), medium (DistFiles/localization/en/BloomMediumPriority.xlf), and low (DistFiles/localization/en/BloomLowPriority.xlf) priority options. If you don't know where it should go, ask.
- Mark new XLF entries translate="no" unless instructed otherwise.
- When adding a new string, do not add it to all of the various language files. Just the one in the "en" subdirectory.
- Don't change the ID of an existing XLF entry unless it is new (marked translate="no"). Instead, mark the old one with a note saying it is "obsolete as of <current Bloom version>" and make a new entry with a different ID. Try to avoid this if possible by keeping the existing ID.
- Don't change the content of an existing XLF entry unless it is new (marked translate="no") or you are sure that the change will not cause problems with existing translations. Instead, mark the old one with a note saying it is "obsolete as of <current Bloom version>" and make a new entry with the new content and a different ID.
- You can find the current version from the `Version` property in `build/Bloom.proj`.
- It's OK not to make XLF entries for strings only used in experimental features, as long as there is fallback English in the code that will be used.

# Commenting
All public methods should have a comment. So should most private ones!

# Git Committing
Always include a good description when creating a git commit.

# Skills
Reusable, task-specific procedures for this repo live in `.github/skills/<name>/SKILL.md`.
When a request matches one of these, READ the matching `SKILL.md` and follow it as the
authoritative procedure (it may have more files alongside it). These may not be auto-loaded
for non-copilot agents, so you may have to open the file yourself.
