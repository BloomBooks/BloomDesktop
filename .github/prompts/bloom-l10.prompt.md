---
mode: agent
description: make a string localizable
---

This project uses the following methods to make strings localizable in react components:
- use an l10n-aware component such as <BloomButton l10nKey="myKey">My Text</BloomButton>, <H1 l10nKey="myKey">My Text</H1>, etc.
- useL10n("myKey", "My Text")

There should be a selected line with a string. If there is not, stop and tell me that I have to select a line with a string to be localized.

# Wrapping the string
If the string is already wrapped in a l10n-aware component or useL10n, go on to the XLIFF portion of these instructions. Otherwise, wrap it in useL10n(english, id, comment_for_translator). Come up with a good ID that goes from general to specific with fullstops in between. Come up with a description that will help translators know the context and what this string is about. For example, change this:
```tsx
// somewhere in Foobar dialog
<button>{Brighten everything}</button>
```
to this:
```tsx
import { useL10n } from "../../react_components/l10nHooks";
// somewhere in Foobar dialog
<button>{useL10n("Brighten everything", "FoobarDialog.BrightenEverything", "Used to make everything brighter")}</button>
```
The `import` above will need the correct path relative to the current file. The file lives at src/BloomBrowserUI/react_components/l10nHooks.ts.

# XLIFF
For each string, we have to have a matching record in one of the xlf files in the /DistFiles/l10n/en folder. There are high (DistFiles/localization/en/BloomHighPriority.xlf), medium (DistFiles/localization/en/Bloom.xlf), and low (DistFiles/localization/en/BloomLowPriority.xlf) priority options. Check all three xliff files for a matching record. If you find it, tell the user where it is. If you don't find it, stop and ask me which priority I want with numbers so I can respond 1,2,or 3. Then create the record in the appropriate file, placing the record next to similar records. For example, here we would want to group the `FoobarDialog` records together.
