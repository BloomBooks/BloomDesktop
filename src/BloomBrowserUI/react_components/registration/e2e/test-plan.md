# Registration Dialog - Test Plan

## Overview
Test the Registration Dialog component in Bloom's component-tester. The dialog has two modes:
1. **Normal mode** - Registration is optional, includes Cancel button
2. **Email Required mode** - For Team Collections, email is mandatory, no Cancel button

**Test Implementation Status**: ✅ All implemented tests passing in `src/BloomBrowserUI/react_components/registration/e2e/`

## Test Scenarios

### 1. Initial Rendering & Layout ✅

**Test**: Dialog renders correctly with all elements ✅
- Verify heading "Please take a minute to register Bloom" is displayed
- Verify all form fields are present and visible:
  - First Name field
  - Surname field
  - Email Address field
  - Organization field
  - "How are you using Bloom?" multiline field
- Verify Register button is present and enabled

**Test**: Email Required mode displays correctly ✅
- Navigate to Email Required story
- Verify team collection warning message is displayed
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
- Verify field shows error state (aria-invalid="true")

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

### 7. "I'm Stuck" Button ✅

**Test**: Button appears after 10 seconds ✅
- Load with showOptOut=true
- Verify "I'm stuck" button is NOT visible initially
- Wait 11 seconds
- Verify "I'm stuck, I'll finish this later" button is now visible

### 8. Edge Cases ✅

**Test**: Very long text doesn't break layout ✅
- Enter 200 character string in First Name
- Verify field accepts it
- Verify dialog remains visible and intact

**Test**: Whitespace-only is invalid ✅
- Enter "   " (spaces only) in First Name
- Click Register
- Verify field shows error state

### 9. Accessibility ✅

**Test**: All fields have proper labels ✅
- Verify First Name field has label
- Verify Surname field has label
- Verify Email Address field has label
- Verify Organization field has label
- Count of accessible fields should be > 0 for each

### 10. Form Submission ✅

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

### 11. Cancel Button ✅

**Test**: Cancel button only in optional mode ✅
- Verify Cancel button is visible when registrationIsOptional=true
- Verify Cancel button is NOT visible when registrationIsOptional=false
- Verify Cancel button appears in normal mode but not in email-required mode

### 12. Field Focus & Tab Order ✅

**Test**: First Name has initial focus ✅
- Load component with pre-filled data
- Wait 1 second
- Verify First Name field is focused

**Test**: Tab moves through fields correctly ✅
- Start at First Name (focused)
- Press Tab
- Verify Surname is now focused
- Press Tab
- Verify Email is now focused
- Press Tab
- Verify Organization is now focused
- Press Tab
- Verify "How are you using" is now focused

### 13. Data Pre-population ✅

**Test**: Pre-populated fields show no errors ✅
- Load component with pre-filled data
- Verify First Name has a value
- Verify Surname has a value
- Verify Email has a value
- Verify Organization has a value
- Verify "How are you using" has a value
- Verify no fields show error states initially

## Implementation Notes

### Fixed Issues

1. **Field values persisting**: Created `StatefulRegistrationContents` wrapper component that maintains state, allowing field values to persist when tests fill them in.

2. **"I'm stuck" button timing**: Implemented 10-second delay in `StatefulRegistrationContents` using `useEffect` with `setTimeout`.

3. **Email validation**: Works correctly now that fields maintain state - invalid emails properly show error indicators.

## Running the Tests

```bash
cd src/BloomBrowserUI/react_components/component-tester
yarn test
```

**Test Results**: ✅ All 50 tests pass successfully!

### Test Files:
- `registration-smoke.e2e.spec.tsx` - 2 basic smoke tests
- `registration-validation.e2e.spec.tsx` - 1 email validation test (legacy)
- `registration-layout.e2e.spec.tsx` - 3 layout and rendering tests
- `registration-fields.e2e.spec.tsx` - 11 field validation tests (First Name, Surname, Organization, How are you using, Edge Cases, Accessibility)
- `registration-email.e2e.spec.tsx` - 25 comprehensive email field tests
- `registration-behavior.e2e.spec.tsx` - 5 form submission, focus, and pre-population tests
- `registration-cancel.e2e.spec.tsx` - 3 cancel button tests

### Email Testing Coverage (25 tests):
**Initial Load Conditions (3 tests)**
- Empty field without errors
- Pre-populated valid email
- Pre-populated invalid email shows error

**Valid Email Formats (7 tests)**
- Standard format (user@domain.com)
- Plus signs for aliases (user+test@domain.com)
- Subdomains (user@mail.example.com)
- Dots in username (first.last@domain.com)
- Numbers (user123@domain456.com)
- Hyphens (user-name@my-domain.com)
- Underscores (user_name@domain.com)

**Invalid Email Formats (6 tests)**
- Missing @ symbol
- Missing domain
- Missing username
- Spaces in email
- Double @ symbols
- Missing TLD

**Typing Behavior (3 tests)**
- Error clears when correcting to valid email
- Can clear email field
- Can modify existing email

**Optional vs Required (3 tests)**
- Optional in normal mode
- Required for team collection shows error when empty
- Valid email accepted when required

**Edge Cases (3 tests)**
- Very long email addresses
- Whitespace-only input
- Trimming whitespace before validation
