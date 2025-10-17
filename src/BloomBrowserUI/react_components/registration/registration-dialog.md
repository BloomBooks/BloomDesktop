# Registration Dialog - Test Plan

## Overview
Test the Registration Dialog component in Bloom's Storybook. The dialog has two modes:
1. **Normal mode** - Registration is optional, includes Cancel button
2. **Email Required mode** - For Team Collections, email is mandatory, no Cancel button

**Test Implementation Status**: ✅ All tests implemented in `src/BloomBrowserUI/tests/e2e/tests/registrationDialog.spec.ts`

## Test Scenarios

### 1. Initial Rendering & Layout ✅

**Test**: Dialog renders correctly with all elements ✅
- Navigate to Normal Dialog story
- Verify dialog title "Register Bloom" is visible
- Verify heading "Please take a minute to register Bloom" is displayed
- Verify all form fields are present and visible:
  - First Name field
  - Surname field
  - Email Address field
  - Organization field
  - "How are you using Bloom?" multiline field
- Verify Register button is present and enabled
- Verify Cancel button is present (normal mode only)
- Verify "I'm stuck" button is NOT visible on initial render
- Verify dialog has approximate width of 400px

**Test**: Email Required mode displays correctly ✅
- Navigate to Email Required story
- Verify team collection warning message is displayed
- Verify Cancel button is NOT present
- Verify Register button is present

### 2. Field Validation - First Name ✅

**Test**: First Name accepts valid input ✅
- Clear First Name field
- Enter "Alice"
- Verify field contains "Alice"
- Enter "Mary Jane" (with space)
- Verify field contains "Mary Jane"
- Enter "O'Brien-Smith" (with special characters)
- Verify field contains "O'Brien-Smith"

**Test**: First Name shows error when empty ✅
- Clear First Name field
- Click Register button
- Wait 500ms
- Verify field shows error state (yellow border or Mui-error class)

### 3. Field Validation - Surname ✅

**Test**: Surname accepts valid input ✅
- Clear Surname field
- Enter "Smith"
- Verify field contains "Smith"
- Enter "Müller-O'Connor" (with special characters)
- Verify field contains "Müller-O'Connor"

**Test**: Surname shows error when empty ✅
- Clear Surname field
- Click Register button
- Wait 500ms
- Verify field shows error state

### 4. Field Validation - Email ✅

**Test**: Email accepts valid formats ✅
- Test valid email: "user@domain.com"
- Test email with plus: "user+test@domain.com"
- Test email with subdomain: "user@sub.domain.com"
- Test email with dots: "first.last@domain.com"
- Each should be accepted and stored in field

**Test**: Email shows error for invalid formats ✅
- Enter "notanemail" (missing @)
- Click Register, verify error state
- Enter "user@" (missing domain)
- Click Register, verify error state
- Enter "@domain.com" (missing username)
- Click Register, verify error state

**Test**: Email is optional in normal mode ✅
- Clear all fields
- Fill First Name, Surname, Organization, How are you using fields
- Leave Email empty
- Click Register
- Verify Email field does NOT show error

### 5. Field Validation - Organization ✅

**Test**: Organization accepts valid input ✅
- Enter "SIL International"
- Verify field contains value
- Enter "SIL International (East Asia)" (with special chars)
- Verify field contains value

**Test**: Organization shows error when empty ✅
- Clear Organization field
- Click Register
- Verify field shows error state

### 6. Field Validation - How are you using Bloom ✅

**Test**: Accepts multiline text ✅
- Enter multiline text: "Creating materials\nFor literacy\nIn multiple languages"
- Verify field contains the text

**Test**: Shows error when empty ✅
- Clear the field
- Click Register
- Verify field shows error state

### 7. Form Submission ✅

**Test**: Dialog does not close with invalid data ✅
- Clear all required fields
- Click Register
- Wait 1 second
- Verify dialog is still visible
- Verify multiple fields show error states

**Test**: All fields show errors when all are invalid ✅
- Clear First Name, Surname, Organization, How are you using fields
- Click Register
- Verify all 4 fields show error states

### 8. "I'm Stuck" Button ✅

**Test**: Button appears after 10 seconds ✅
- Load Normal Dialog story
- Verify "I'm stuck" button is NOT visible initially
- Wait 11 seconds
- Verify "I'm stuck, I'll finish this later" button is now visible

### 9. Cancel Button ✅

**Test**: Cancel button only in optional mode ✅
- Navigate to Normal Dialog story
- Verify Cancel button is visible
- Navigate to Email Required story
- Verify Cancel button is NOT visible

### 10. Field Focus & Tab Order ✅

**Test**: First Name has initial focus ✅
- Navigate to Normal Dialog story
- Wait 1 second
- Verify First Name field is focused

**Test**: Tab moves through fields correctly ✅
- Start at First Name (focused)
- Press Tab
- Verify Surname is now focused
- Press Tab
- Verify Email is now focused

### 11. Data Pre-population ✅

**Test**: Pre-populated fields show no errors ✅
- Navigate to Normal Dialog story (has pre-filled data)
- Verify First Name has a value
- Verify no fields show error states initially

### 12. Edge Cases ✅

**Test**: Very long text doesn't break layout ✅
- Enter 200 character string in First Name
- Verify field accepts it
- Verify dialog remains visible and intact

**Test**: Whitespace-only is invalid ✅
- Enter "   " (spaces only) in First Name
- Click Register
- Verify field shows error state

### 13. Accessibility ✅

**Test**: All fields have proper labels ✅
- Verify First Name field has label
- Verify Surname field has label
- Verify Email Address field has label
- Verify Organization field has label
- Count of accessible fields should be > 0 for each

## Test Data

### Valid Data
```
firstName: "John"
surname: "Doe"
email: "john.doe@example.com"
organization: "SIL International"
usingFor: "Creating literacy materials"
```

### Invalid Emails to Test
- "notanemail"
- "user@"
- "@domain.com"
- "double@@domain.com"
- "spaces in@email.com"

### Edge Cases
- Very long name: 200 character string
- Special characters: "O'Brien", "Jean-Claude", "Müller"
- Whitespace only: "   "

## Running the Tests

```bash
# Option 1: Start storybook manually, then run tests
cd src/BloomBrowserUI
yarn storybook
# In another terminal:
cd src/BloomBrowserUI/tests/e2e
npx playwright test

# Option 2: Let playwright manage storybook (slower)
cd src/BloomBrowserUI/tests/e2e
npx playwright test
```
