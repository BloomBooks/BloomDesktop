This project has a web front-end at src/BloomBrowserUI.
The front-end uses yarn 1.22.22. Never ever use npm.

# Code Style

- Always use arrow functions and function components in React
- do not destructure props
- do not define a props data type unless it is huge
- example: export const SomeComponent: React.FunctionComponent<{initiallySelectedGroupIndex: number;> = (props) => {...}

- Avoid removing existing comments.
- Avoid adding a comment like "// add this line".

- Style elements using the css macro from @emotion/react directly on the element being styled, using the css prop. E.g. `<div css={css`color:red`}>`

- For Typescript coding style, see ./src/BloomBrowserUI/AGENTS.md

# Testing

- Fail Fast. Don't write code that silently works around failed dependencies. If a dependency is missing we should fail. Javascript itself will fail if we try to use a missing dependency, and that's fine. E.g. if you expect a foo to be defined, don't write "if(foo){}". Just use foo and if it's null, fine, we'll get an error, which is good.


# Terminal
The vscode terminal often loses the first character sent from copilot agents. So if you send "cd" it might just say "bash: d: command not found". Try prefixing commands with a space.

# Don't run build
It is vital that you not run `yarn build` unless instructed to. If there is already a "--watch" build running, you will wreck it and waste the developer's time. You are welcome to `yarn lint` if you want to check for errors without building.

# Localization
- Localizations for translatable strings are kept in DistFiles/localizations; new ones are initially added to one of the files in the "en" subdirectory
- Mark new XLF entries translate="no"
