# Registration Dialog E2E Test Plan

## Test Environment
- **Component**: Registration Dialog (`registrationDialog.tsx`)
- **Test Framework**: Playwright
- **Test Target**: Storybook stories at `http://localhost:58886`
- **Stories**:
  - Normal Dialog (optional registration)
  - Email Required (for team collections)

---

## Test Categories

### 1. Initial Rendering & Layout
- [ ] Dialog renders with correct title "Register Bloom"
- [ ] Heading text displays "Please take a minute to register Bloom."
- [ ] All form fields are present and visible:
  - [ ] First Name field
  - [ ] Surname field
  - [ ] Email Address field
  - [ ] Organization field
  - [ ] "How are you using Bloom?" multiline field (3 rows)
- [ ] Register button is present and visible
- [ ] Cancel button is present when registration is optional
- [ ] Cancel button is NOT present when registration is required
- [ ] "I'm stuck" button is NOT visible on initial render (appears after 10 seconds)
- [ ] No close/X button in dialog title (preventCloseButton=true)
- [ ] Dialog width is 400px

### 2. Email Required Mode (Team Collection)
- [ ] Warning message displays: "You will need to register this copy of Bloom with an email address before participating in a Team Collection"
- [ ] Warning message is NOT displayed in normal mode
- [ ] Cancel button is NOT present
- [ ] Dialog cannot be closed by clicking backdrop
- [ ] Dialog cannot be closed by pressing Escape key

### 3. Field Validation - First Name
- [ ] Accepts valid text input
- [ ] Accepts names with spaces
- [ ] Accepts special characters (apostrophes, hyphens, etc.)
- [ ] Trims whitespace for validation
- [ ] Shows error state when empty and Register is clicked
- [ ] Shows error state immediately when filled then cleared
- [ ] Error styling is yellow (kBloomGold)
- [ ] Field "jiggles" (drawAttention animation) when validation fails on submit

### 4. Field Validation - Surname
- [ ] Accepts valid text input
- [ ] Accepts names with spaces
- [ ] Accepts special characters
- [ ] Trims whitespace for validation
- [ ] Shows error state when empty and Register is clicked
- [ ] Shows error state immediately when filled then cleared
- [ ] Error styling is yellow (kBloomGold)
- [ ] Field "jiggles" when validation fails on submit

### 5. Field Validation - Email Address
- [ ] Accepts valid email format (user@domain.com)
- [ ] Accepts valid email with "+" (user+bloom@domain.com)
- [ ] Accepts emails with international characters (用户@例子.广告)
- [ ] Accepts emails with subdomains (user@sub.domain.com)
- [ ] Accepts emails with dots in username (first.last@domain.com)
- [ ] Rejects invalid formats:
  - [ ] Missing @ symbol
  - [ ] Missing domain
  - [ ] Missing TLD (.com, .org, etc.)
  - [ ] Double @@ symbols
  - [ ] Spaces in email
- [ ] Email is optional in normal mode (can be empty)
- [ ] Email is required in team collection mode
- [ ] Shows error state for invalid format even if optional
- [ ] Shows error styling when required but empty
- [ ] Field "jiggles" when validation fails on submit
- [ ] Email field can be disabled (mayChangeEmail=false)
- [ ] When disabled, label changes to "Check in to change email"
- [ ] When disabled, field shows as disabled/grayed out

### 6. Field Validation - Organization
- [ ] Accepts valid text input
- [ ] Accepts alphanumeric characters
- [ ] Accepts special characters and punctuation
- [ ] Trims whitespace for validation
- [ ] Shows error state when empty and Register is clicked
- [ ] Error styling is yellow (kBloomGold)
- [ ] Field "jiggles" when validation fails on submit

### 7. Field Validation - How are you using Bloom?
- [ ] Accepts multiline text input
- [ ] Displays 3 rows initially
- [ ] Allows text to wrap to multiple lines
- [ ] Trims whitespace for validation
- [ ] Shows error state when empty and Register is clicked
- [ ] Error styling is yellow (kBloomGold)
- [ ] Field "jiggles" when validation fails on submit

### 8. Form Submission - Valid Data
- [ ] Register button is enabled at all times
- [ ] Clicking Register with all valid data:
  - [ ] Calls postJson to "registration/userInfo" endpoint
  - [ ] Sends all form data (firstName, surname, email, organization, usingFor)
  - [ ] Calls onSave callback with isValidEmail result
  - [ ] Closes the dialog
- [ ] Dialog closes only after successful API call

### 9. Form Submission - Invalid Data
- [ ] Clicking Register with empty fields:
  - [ ] Does NOT submit form
  - [ ] Does NOT close dialog
  - [ ] Increments submitAttempts counter
  - [ ] Shows error state on all invalid fields
  - [ ] All invalid fields "jiggle" simultaneously
  - [ ] Does NOT call API endpoint
- [ ] Clicking Register with partial data:
  - [ ] Shows errors only on empty/invalid fields
  - [ ] Valid fields do not show errors
- [ ] Each click on Register with invalid data triggers new validation

### 10. "I'm Stuck" Opt-Out Button
- [ ] Button is NOT visible on initial render
- [ ] Button appears after exactly 10 seconds
- [ ] Button text: "I'm stuck, I'll finish this later."
- [ ] Button is left-aligned in dialog footer
- [ ] Button has small font size (10px)
- [ ] Clicking "I'm stuck":
  - [ ] Clears email if invalid
  - [ ] Saves all other data even if incomplete
  - [ ] Calls API endpoint
  - [ ] Closes dialog
- [ ] Button does NOT validate form before saving
- [ ] Timer resets if dialog is closed and reopened

### 11. Cancel Button Behavior
- [ ] Cancel button only appears when registrationIsOptional=true
- [ ] Clicking Cancel closes the dialog
- [ ] Cancel does NOT save any data
- [ ] Cancel does NOT call API endpoint
- [ ] Cancel button has standard styling

### 12. Dialog Close Behavior - Optional Registration
- [ ] Pressing Escape key closes dialog
- [ ] Clicking backdrop (outside dialog) closes dialog
- [ ] Cancel button closes dialog
- [ ] No data is saved when closing without Register

### 13. Dialog Close Behavior - Required Registration
- [ ] Pressing Escape does NOT close dialog
- [ ] Clicking backdrop does NOT close dialog
- [ ] No Cancel button available
- [ ] Only way to close is successful Register or "I'm stuck" button
- [ ] Title close button (X) is prevented

### 14. Field Focus & Tab Order
- [ ] First Name field has autofocus on dialog open
- [ ] Tab key moves through fields in correct order:
  - [ ] First Name → Surname → Email → Organization → How are you using
- [ ] Tab order skips disabled email field
- [ ] Shift+Tab moves backwards through fields
- [ ] Tab from last field moves to buttons
- [ ] Tab order includes Register and Cancel buttons

### 15. Data Persistence & Pre-population
- [ ] Dialog fetches existing user info from GET "registration/userInfo"
- [ ] Pre-existing data populates fields on open:
  - [ ] firstName field
  - [ ] surname field
  - [ ] email field
  - [ ] organization field
  - [ ] usingFor field
- [ ] hadEmailAlready flag is loaded
- [ ] mayChangeEmail setting is fetched from "teamCollection/mayChangeRegistrationEmail"
- [ ] Pre-populated fields do NOT show validation errors initially
- [ ] Changes to pre-populated data are tracked

### 16. Initial State Validation (Various Starting Conditions)
- [ ] **Empty email state**:
  - [ ] Empty email field shows no error initially
  - [ ] Empty email is valid in normal mode
  - [ ] Empty email triggers error in team collection mode when submitting
  - [ ] Empty email field can accept input normally
- [ ] **Invalid email state** (pre-populated with invalid format):
  - [ ] Invalid email shows no error initially (before submit)
  - [ ] Invalid email shows error state when user clicks Register
  - [ ] Invalid email can be corrected by user
  - [ ] Correcting invalid email clears error state
  - [ ] Examples to test: "notanemail", "user@", "@domain.com", "user space@domain.com"
- [ ] **Partially complete form state**:
  - [ ] Some fields empty, others filled - no errors shown initially
  - [ ] Submit triggers errors only on empty required fields
  - [ ] Valid filled fields maintain their values
- [ ] **All fields empty state**:
  - [ ] Opening with completely empty data shows no errors
  - [ ] First submit attempt shows all fields in error
  - [ ] User can fill fields one by one and errors clear
- [ ] **Pre-populated with valid data state**:
  - [ ] All fields show data without errors
  - [ ] Register button works immediately
  - [ ] User can modify any field
- [ ] **Email disabled state** (mayChangeEmail=false):
  - [ ] Email field is disabled/grayed out
  - [ ] Email field cannot be edited
  - [ ] Label shows "Check in to change email"
  - [ ] Other fields remain editable
  - [ ] Validation skips disabled email field
- [ ] **Mixed valid/invalid state**:
  - [ ] Valid firstName, invalid email - only email errors on submit
  - [ ] Empty required field + invalid email - both show errors
  - [ ] Fixing one error doesn't affect other error states

### 17. Visual Feedback & Animations
- [ ] Invalid fields show yellow outline (not red)
- [ ] "drawAttention" animation plays when validation fails:
  - [ ] Animation duration is ~1 second
  - [ ] Animation is a "jiggle" effect
  - [ ] Animation clears after completing
- [ ] Error state persists after animation completes
- [ ] Hover states work on buttons
- [ ] Focus rings visible on keyboard navigation
- [ ] Field labels float correctly with Material-UI behavior

### 17. Accessibility
- [ ] All fields have proper labels
- [ ] Labels are localized (l10nKey attributes)
- [ ] Error states announced to screen readers
- [ ] Dialog has proper ARIA role
- [ ] Keyboard navigation works completely
- [ ] Focus trap keeps focus within dialog
- [ ] Disabled email field has proper ARIA attributes

### 18. Edge Cases & Error Handling
- [ ] Submitting with only whitespace in fields shows errors
- [ ] Very long text in fields doesn't break layout
- [ ] Rapid clicking Register doesn't cause multiple submissions
- [ ] API failure doesn't crash dialog (stays open)
- [ ] Dialog handles missing/null user info gracefully
- [ ] Email validation handles edge cases:
  - [ ] Email with + symbol (user+tag@domain.com)
  - [ ] Email with numbers (user123@domain.com)
  - [ ] Very long email addresses
  - [ ] Multiple dots in domain (user@sub.sub.domain.com)

### 19. Responsive Behavior
- [ ] Dialog maintains 400px width
- [ ] Fields don't overflow dialog bounds
- [ ] "I'm stuck" button doesn't wrap awkwardly
- [ ] Multiline "How are you using" field scrolls if content exceeds 3 rows
- [ ] Bottom buttons row stays at bottom
- [ ] mustRegisterText width adjusts to button width

### 20. Multiple Open/Close Cycles
- [ ] Opening dialog second time shows fresh validation state
- [ ] Closing without saving doesn't persist changes
- [ ] Opening after "I'm stuck" save shows partial data
- [ ] "I'm stuck" timer resets on each open
- [ ] submitAttempts counter resets on reopen
- [ ] Error states clear when dialog reopens

### 21. Integration with Parent Context
- [ ] onSave callback is called with correct isValidEmail boolean
- [ ] Dialog respects registrationIsOptional prop
- [ ] Dialog respects emailRequiredForTeamCollection prop
- [ ] Team collection context (useIsTeamCollection) affects email requirement
- [ ] External props override internal defaults

### 22. Localization
- [ ] All text uses l10n keys for translation
- [ ] Parameter substitution works ({0} replaced with "Bloom")
- [ ] Field labels are localized
- [ ] Button text is localized
- [ ] Error messages respect localization
- [ ] Localized text doesn't break layout

---

## Test Data Sets

### Valid Test Data
```json
{
  "firstName": "John",
  "surname": "Doe",
  "email": "john.doe@example.com",
  "organization": "SIL International",
  "usingFor": "Creating literacy materials for minority language communities"
}
```

### Invalid Email Formats to Test
- `notanemail`
- `missing@domain`
- `@nodomain.com`
- `double@@domain.com`
- `spaces in@email.com`
- `nodomain@`

### International Email to Test
- `用户@例子.广告`
- `josé@español.com`
- `françois@français.fr`

### Edge Case Data
- Very long names (100+ characters)
- Names with special characters: `O'Brien`, `Jean-Claude`, `Müller`
- Organizations with special characters: `SIL International (East Asia)`
- Multi-paragraph text in "How are you using" field

---

## Notes for Test Implementation
- Use data-testid or stable selectors for element identification
- Mock API endpoints: `registration/userInfo`, `teamCollection/mayChangeRegistrationEmail`
- Test both story variants: Normal and Email Required
- Consider visual regression tests for error states and animations
- Test timing-dependent features (10-second timer) with clock mocking
- Verify API payloads match expected schema

## Observations from Storybook Exploration
✅ **Successfully accessed both stories**:
- `http://localhost:58886/?path=/story/misc-dialogs-registrationdialog--normal-story`
- `http://localhost:58886/?path=/story/misc-dialogs-registrationdialog--email-required-story`

✅ **Confirmed behaviors**:
- "I'm stuck" button appears after ~10 seconds (observed in Storybook)
- Cancel button only appears in "Normal Dialog" story
- No Cancel button in "Email required" story
- Team collection warning text displays correctly in email required mode
- All fields are present and labeled correctly
- Fields come pre-populated with test data (John Hatton, john_hatton@sil.org, etc.)

⚠️ **Known issues in Storybook**:
- API calls throw errors (postMessage undefined) - need proper mocking in tests
- This won't affect real e2e tests as we'll mock the API properly

📍 **Story URLs for testing**:
- Normal: `/story/misc-dialogs-registrationdialog--normal-story`
- Email Required: `/story/misc-dialogs-registrationdialog--email-required-story`

---

## Playwright Agents Setup COMPLETED ✅

**Location**: `src/BloomVisualRegressionTests/`

**What was done**:
1. ✅ Upgraded Playwright to v1.56.1 (latest with AI agents support)
2. ✅ Initialized Playwright agents with `yarn playwright init-agents --loop=vscode`
3. ✅ Created agent definitions in `.github/chatmodes/`:
   - 🎭 planner.chatmode.md - Explores app and creates test plans
   - 🎭 generator.chatmode.md - Transforms plans into Playwright tests
   - 🎭 healer.chatmode.md - Auto-repairs failing tests
4. ✅ Created `seed.spec.ts` - Bootstrap test that navigates to Registration Dialog stories
5. ✅ Created `specs/registration-dialog.md` - Detailed test plan in agent-friendly format
6. ✅ Created `playwright.config.ts` - Configured to auto-start Storybook
7. ✅ Created `tests/` directory for generated tests

**Next Steps** (for AI or developer):
1. Use the 🎭 generator agent to convert `specs/registration-dialog.md` into executable tests
2. Run the generated tests: `yarn playwright test`
3. Use the 🎭 healer agent to fix any failing tests automatically
4. Iteratively add more test scenarios to the spec and regenerate

**How to use the agents** (in VS Code with Copilot):
```
# To generate tests from the spec:
"🎭 generator, please generate tests from specs/registration-dialog.md"

# To heal a failing test:
"🎭 healer, please fix the failing test [test-name]"

# To expand test coverage:
"🎭 planner, explore the Email Required mode and create additional test scenarios"
```

**Files ready for test generation**:
- `seed.spec.ts` - Working seed test
- `specs/registration-dialog.md` - Comprehensive test plan with 13 categories, 30+ test scenarios
- Agent definitions ready to use
