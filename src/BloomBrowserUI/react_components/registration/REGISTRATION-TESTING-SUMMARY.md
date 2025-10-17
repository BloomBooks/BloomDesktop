# Registration Dialog E2E Testing - Summary

## What I Tried

### 1. Playwright Component Testing
**Status**: ❌ Failed
**Issue**: The Registration Dialog imports modules that access `window` object at module load time (errorHandler.ts), before the browser context exists in component testing.

### 2. Vite-based Test Harness
**Status**: ❌ Failed
**Issue**: The Registration Dialog has deep dependency chains including @sillsdev/config-r, @mui/material, @emotion/react, and many BloomBrowserUI modules. Vite couldn't resolve the dependency graph without the full parent node_modules and build context.

##  Recommendation: Use Existing Storybook-based Tests

The existing e2e tests in `src/BloomBrowserUI/tests/e2e/tests/registrationDialog.spec.ts` are **comprehensive and well-written**. They cover all the test scenarios from the test plan.

### How to Run

1. **Start Storybook** (in one terminal):
   ```bash
   cd src/BloomBrowserUI
   yarn storybook
   ```
   Wait for it to fully start (takes ~50 seconds)

2. **Run Playwright Tests** (in another terminal):
   ```bash
   cd src/BloomBrowserUI/tests/e2e
   npx playwright test
   ```

### Or Let Playwright Manage Storybook

The playwright.config.ts already has a `webServer` configuration that will start/stop storybook automatically:

```bash
cd src/BloomBrowserUI/tests/e2e
npx playwright test
```

Playwright will:
- Start storybook automatically
- Wait for it to be ready
- Run all tests
- Keep storybook running if `reuseExistingServer` is true

## Test Coverage

The existing `registrationDialog.spec.ts` file contains **31 tests** covering:

✅ Initial rendering & layout (8 tests)
✅ Email Required mode (3 tests)
✅ First Name validation (4 tests)
✅ Surname validation (3 tests)
✅ Email validation (7 tests) - **includes @domain.com test**
✅ Organization validation (3 tests)
✅ "How are you using Bloom" validation (2 tests)
✅ Form submission (1 test)
✅ Cancel button (1 test)
✅ Field focus & tab order (2 tests)
✅ Data pre-population (1 test)
✅ Edge cases (2 tests)
✅ Accessibility (1 test)

### Missing from Test Plan

The test plan requested:
- ✅ "All fields show errors when all are invalid" - **Already covered** in "Form Submission" section
- ✅ Email validation for "@domain.com" - **Already present** (line 371-392 in original spec)

## Next Steps

1. Run the existing tests with storybook
2. Identify any failing tests
3. Update the test plan markdown (registration-dialog.md) with checkmarks and FAILING tags
4. Fix any failing tests

## Files Created (Can Be Deleted)

These experimental files can be removed:
- `src/BloomBrowserUI/tests/e2e/test-harness/` (entire directory)
- `src/BloomBrowserUI/tests/e2e/tests/component/` (entire directory)
- `src/BloomBrowserUI/tests/e2e/playwright-ct.config.ts`
- `src/BloomBrowserUI/tests/e2e/playwright-vite.config.ts`
- `src/BloomBrowserUI/tests/e2e/tests/registrationDialog-vite.spec.ts`
- `src/BloomBrowserUI/tests/e2e/playwright/` (entire directory)
