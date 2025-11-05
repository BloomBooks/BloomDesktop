# Front-end
## Directory
When working in the front-end, cd to src/BloomBrowserUI. This uses yarn 1.22.22. Never ever use npm.

## Stack
- typescript
- react
- MUI
- Emotion
- yarn 1.22.22
- Never use npm commands

- Never use CDNs. This is an offline app.

## Code Style

- Always use arrow functions and function components in React
- When creating new components, prefer defining the props inline instead of in a separate `IProps` type, unless it is huge.
CORRECT: export const SomeComponent: React.FunctionComponent<{initiallySelectedGroupIndex: number;> = (props) => {...}

- Style elements using the css macro from @emotion/react directly on the element being styled, using the css prop. E.g. `<div css={css`color:red`}>`

- Avoid creating const variables that just mirror react component `props`. `props.foo` is easy to read and understand where `foo` comes from.
