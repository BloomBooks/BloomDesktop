---
description: setup ui tests
---
We have a test system explained at src/BloomBrowserUI/react_components/component-tester/README.md.

First, decide if we are in the Setup or Implementation stage.

# Setup Stage
If you do not see a component_tests/ directory, it means we are not set up yet. For an example, look at src/BloomBrowserUI/react_components/registration and src/BloomBrowserUI/react_components/registration/component-tests as an example. You need to intercept some api calls using src/BloomBrowserUI/react_components/component-tester/apiInterceptors.ts.  In the directory of the component (not in component-tests/), create test.sh and manual.sh scripts to run the tests and open the component in a browser for manual testing.If it doesn't exist already, make a <componentname>-ui-test-plan.md for this. Keeps it small-- during setup we are not ready for complete testing, but proving that you can make the component show in a browser and do one minimal thing. Later, I will direct you to to add more tests to the plan, but not now. Once you have the plan, work your way through it, checking off items as you go, running the tests along the way.
* If you are unclear about how to organize tests in different files, make a proposal and ask me.

# Implementation Stage

## Troubleshooting
If you need to see what the browser sees, you may use the chrome-devtools mcp. You can access the console, take screenshots, and inspect elements. If you don't have access to that mcp, ask me to install/enable it.

## Refactoring to make testing easier
Sometimes the top level component is not readily testable. In that case it might be appropriate to refactor out a core that is easily testable. Do not do this without discussing it with me first. Just stop and make a proposal. Do not refactor any code except for the test code itself without prior approval.

## Guidelines for writing the tests
* If you want to make a mock, stop and ask me.
* Avoid using timed waits like page.waitForTimeout(1000). If there is no other way, you must discuss it with me first and then if I approve, document why it is necessary in a comment.
* Feel free to add data-test-id attributes to elements in the component under test to make them easier to find. Avoid using css to finding things.
* Keep the tests well factored with common code going to a test-helpers.ts file.

