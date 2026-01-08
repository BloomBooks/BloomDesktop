---
mode: agent
description: make a string localizable
---

This project uses the following methods to make strings localizable in react components:
- use an l10n-aware component such as <BloomButton l10nKey="myKey">My Text</BloomButton>, <H1 l10nKey="myKey">My Text</H1>, etc.
- useL10n("myKey", "My Text")

There should be a selected line with a string. If there is not, stop and tell me that I have to select a line with a string to be localized.

# Wrapping the string
If the string is already wrapped in a l10n-aware component or useL10n, go on to the XLIFF portion of these instructions. Otherwise, wrap it in useL10n(english, id).```tsx
// somewhere in Foobar dialog
<button>{Brighten everything}</button>
```
to this:
```tsx
import { useL10n } from "../../react_components/l10nHooks";
// somewhere in Foobar dialog
<button>{useL10n("Brighten everything", "FoobarDialog.BrightenEverything")}</button>
```
The `import` above will need the correct path relative to the current file. The file lives at src/BloomBrowserUI/react_components/l10nHooks.ts.

* normally, don't fill in l10nComment parameter when using useL10n, that just clutters the code. We will put context in the xliff file instead. However if you want to use the later parameters, then you have to put something in that parameter.

# Adding to XLIFF files
For each string, we have to have a matching record in one of the xlf files in the /DistFiles/l10n/en folder. There are high (DistFiles/localization/en/BloomHighPriority.xlf), medium (DistFiles/localization/en/Bloom.xlf), and low (DistFiles/localization/en/BloomLowPriority.xlf) priority options. Check all three xliff files for a matching record. If you find it, tell the user where it is. If you don't find it, stop and ask me which priority I want with numbers so I can respond 1,2,or 3. Then create the record in the appropriate file, placing the record next to similar records. For example, here we would want to group the `FoobarDialog` records together.

# Default to not ready for translation
Add a `translate="no"` attribute to new records unless I tell that these strings are ready for translation.

## String ID
the string id may be used by translators as they try to understand context or translate a group of related strings. So make sure it is logical and hierarchical. If the string is a tooltip, make that the last part of the id. E.g. LinkTargetChooser.URL.Paste.Tooltip, not LinkTargetChooser.Tooltip.Paste.

## Expose ID to translators
Add a note like this: `<note>ID: LinkTargetChooser.URL.Paste.Tooltip</note>`

## Add comments for translators
Although we don't want to fill in l10nComment in useL10n, we do want to fill in the note field to give context to translators. They don't know where the string appears in the UI, they also might need some explanation of what it means. For example, for the above string, we might add a note like `<note>This is the text on a button in the Foobar dialog that brightens all images in the current book.</note>`

# Tips
* Never use the word "Aria" in ids or comments. Translators don't know what that means.
* Stop processing immediately if I haven't told you what priority we want. After you have the priority, then you can continue.
