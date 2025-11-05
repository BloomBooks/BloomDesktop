# BookGridSetup Component Tests

This directory contains UI tests for the BookGridSetup component using Playwright.

## Setup

The component uses a clever pattern to handle the `onLinksChanged` callback in tests:

- **Production use**: `onLinksChanged` is a function: `(links: Link[]) => void`
- **Test use**: `onLinksChanged` is a string URL like `"testapi/bookGridSetup/linksChanged"`

When a string is provided, the component POSTs the links data to that URL, which is intercepted using `preparePostReceiver` from the apiInterceptors. This solves the problem of functions not being JSON-serializable for test setup.

## Running Tests

```bash
# Run automated tests (headless)
./test.sh

# Open component manually in browser for testing
./manual.sh
```

## Files

- `component-tester.config.ts` - Configuration for the component tester
- `test-helpers.ts` - Helper functions for tests
- `bookgridsetup-basic.uitest.ts` - Basic interaction tests (2 tests)
- `bookgridsetup-extended.uitest.ts` - Extended tests covering all functionality (14 tests)
- `bookgridsetup-ui-test-plan.md` - Test plan document

## Writing Tests

Use the helper functions from `test-helpers.ts`:

```typescript
const receiver = await setupBookGridSetupComponent(page, {
    sourceBooks: testBooks,
    links: [],
});

// Interact with component...
await page.getByTestId("source-book-book-id").click();

// Verify the callback was called with correct data
const links = await receiver.getData();
expect(links).toHaveLength(1);
```

## Test IDs

The component uses these test-id attributes:
- `source-book-{id}` - Books in the source list
- `target-book-{id}` - Books in the target list
- `remove-book-button` - Remove button that appears on hover over target books

## Test Coverage

The test suite includes:
- **Basic Tests** (2 tests): Component rendering and basic add functionality
- **Extended Tests** (15 tests):
  - Add All Books functionality (3 tests)
  - Remove books from target list (3 tests)
  - Disabled states (3 tests)
  - Multiple books management (2 tests)
  - Edge cases (4 tests, including folder name vs title display)

All 17 tests are passing.

## Notes

- Remove buttons are hidden by CSS (`display: none`) until hover. Tests use `.hover()` to show the button before clicking.
- The component automatically selects the next available book after adding one, making sequential additions easier.
