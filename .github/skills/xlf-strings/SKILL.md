---
name: xlf-strings
description: Add or review localizable strings in Bloom XLF files
---

Apply this skill whenever you add a new localizable string to the codebase, modify an existing one, or review XLF files as part of a task.

## Files

Localizable strings live in `DistFiles/localization/en/`. Only ever add or change strings in the **English** subdirectory — never touch the other language folders.

There are three priority files. Choose based on what the string labels:

| File | Use for |
|---|---|
| `Bloom.xlf` | The most important things in our UI that users must understand
| `BloomMediumPriority.xlf` | Secondary options, help text, feature-specific instructions, important error messages |
| `BloomLowPriority.xlf` | Rarely-seen text like error messages |

Strings that are only meant to be seen by developers or if code bugs occur should not be localized. Add comments explaining why they are not.

## Adding a new entry

1. **Ask the user which priority file to use, with your recommendation.** Explain why you prefer one based on the table above. You may present several strings as a single question if they occur in the same context.
2. Add the entry in the chosen file:

```xml
<trans-unit id="Namespace.EntryName" translate="no">
  <source xml:lang="en">The English text</source>
  <note>ID: Namespace.EntryName</note>
</trans-unit>
```

- Always mark new entries `translate="no"` unless instructed otherwise.
- Choose an ID in the form `Namespace.EntryName` that matches the feature area.

## Translator context notes

After the `<note>ID: ...</note>` line, add a **second `<note>`** whenever a translator would not have enough context from the string alone. This is required when the string is any of:

- A generic or short word ("Source", "Search", "More info", "Not Ready")
- A sentence fragment assembled with other strings
- A numbered step in a sequence
- Link text
- A string where product names, placeholders, or case constraints matter

The note should state: what UI element it labels, where it appears in the UI, and any constraints (e.g. "appears mid-sentence, should be lowercase", "step N of M in X instructions", "'Bloom' is a product name and must not be translated", "{0} is replaced with a count").

Example with context note:
```xml
<trans-unit id="ImageLibrary.PixabayStep4" translate="no">
  <source xml:lang="en">Paste it below</source>
  <note>ID: ImageLibrary.PixabayStep4</note>
  <note>Step 4 in the Pixabay API key instructions. "It" refers to the API key copied in step 3; "below" refers to the text input field below the instruction list.</note>
</trans-unit>
```

## Modifying an existing entry

- **Never change the ID** of an entry that is not new (i.e. not marked `translate="no"`). Changing an ID loses all existing translations.
- **Never change the source text** of a translated entry unless you are certain it won't invalidate existing translations.
- If you need to change the text or ID: mark the old entry with a `<note>` saying `obsolete as of <version>` and create a **new entry** with a new ID and the updated text. Find the current version in the `Version` property in `build/Bloom.proj`. Avoid this when possible.

## Reviewing XLF changes in a PR

When reviewing a PR that touches XLF files:

1. Check that every new entry has `translate="no"`.
2. Check that new entries are in the right priority file (see table above).
3. Check that every entry whose context isn't obvious from the string alone has a translator context note.
4. Check that no existing translated entry has had its ID or source text changed without the obsolete/new-entry pattern.
