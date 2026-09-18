---
name: component-test
description: Set up or extend Playwright UI tests for a React component under src/BloomBrowserUI/react_components using the component-tester harness (*.uitest.ts). Use when asked to "set up ui tests", "add component tests", "make this component testable", or to write a component test plan.
argument-hint: "the component folder, and whether this is first-time setup or adding tests"
---

# Component UI tests

The test system is explained in `src/BloomBrowserUI/react_components/component-tester/README.md`;
read it first. Then decide whether you are in the **setup** or the **implementation** stage.

## Setup stage

If the component has no `component-tests/` directory, it is not set up yet. Use
`src/BloomBrowserUI/react_components/registration` and its `component-tests/` folder as the
example. You will need to intercept some API calls with
`src/BloomBrowserUI/react_components/component-tester/apiInterceptors.ts`.

- In the component's own directory (not in `component-tests/`), create `test.sh` and `manual.sh`
  scripts that run the tests and open the component in a browser for manual testing.
- If there is no `<componentname>-ui-test-plan.md` yet, make one. Keep it small: during setup
  the goal is proving the component shows in a browser and does one minimal thing, not complete
  coverage. The developer will direct you to add more later.
- Work through the plan, checking items off as you go and running the tests along the way.
- If you are unclear how to organize tests across files, make a proposal and ask.

## Implementation stage

**Troubleshooting.** To see what the browser sees (console, DOM, screenshots), use a CDP client;
the `bloom-automation` skill describes attaching Playwright over CDP. If you have no browser
tooling available, stop and ask the developer to enable some.

**Refactoring to make testing easier.** Sometimes the top-level component is not readily
testable and a core should be extracted. Do not do that without discussing it first: stop and
make a proposal. Do not refactor any non-test code without prior approval.

**Guidelines for the tests.**

- If you want to make a mock, stop and ask.
- Avoid timed waits like `page.waitForTimeout(1000)`. If there is no other way, discuss it first
  and, once approved, document why in a comment.
- Feel free to add `data-test-id` attributes to elements in the component under test. Avoid
  finding things by CSS.
- Keep the tests well factored, with common code in a `test-helpers.ts` file.
- Do not check styles as a way to know the status of something; that is fragile. Have the
  component expose a class or attribute instead.
