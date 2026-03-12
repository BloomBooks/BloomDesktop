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

- Avoid removing existing comments.
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

# Don't run build
It is vital that you not run `yarn build` unless instructed to. If there is already a "--watch" build running, you will wreck it and waste the developer's time. You are welcome to `yarn lint` if you want to check for errors without building.

# Localization
- Localizations for translatable strings are kept in DistFiles/localizations; new ones are initially added to one of the files in the "en" subdirectory
- Mark new XLF entries translate="no"
