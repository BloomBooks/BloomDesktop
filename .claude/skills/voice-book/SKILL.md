---
name: voice-book
description: Turn a Bloom book into a talking book with ElevenLabs text-to-speech, one language at a time, from a local book folder or a bloomlibrary.org book URL. Use when asked to voice, narrate, or add AI audio to a Bloom book.
argument-hint: "book (folder or bloomlibrary.org URL), language tag, voice"
---

# Voice a Bloom book with ElevenLabs

`voiceBook.mjs` records each text box of one language as a single ElevenLabs clip and writes the
markup Bloom's spreadsheet import writes for audio recorded "By Whole Text Box" and then split
(`SpreadsheetImporter.AddAudioAsync`). ElevenLabs' character timestamps supply the sentence
boundaries, so Bloom highlights each sentence as it is read. Sentences come from Bloom's own
splitter and checksums from Bloom's own `getChecksum`, both bundled from `src/BloomBrowserUI` at
run time, so the Talking Book tool sees the recordings as current. Paths below are
repo-root-relative; commands are bash.

## 1. Get the book folder

A book already in a collection: use its folder. A book on bloomlibrary.org: download it into a
collection of its own, as Bloom's "Download into Bloom" would:

```bash
node .claude/skills/voice-book/downloadBook.mjs "https://bloomlibrary.org/language:swh/book/HRahJAASYF" "D:/some-folder"
# prints { collectionFile, bookFolder }
```

Done when you have a book folder holding the book's `.htm`.

## 2. Make sure Bloom is not holding the book

Bloom re-saves the book it has selected from its in-memory copy and silently overwrites outside
edits. Close that Bloom, or select a different book in it, before running the script. Opening a
book can also rename its folder to match the title (an old Bloom Library book came back named
after its Swahili title), so list the collection folder again after Bloom has had the book.

## 3. Choose the voice and model

Ask the user which voice to use for this language, unless they already said. Voice ids from the
account:

```powershell
$h=@{'xi-api-key'=[Environment]::GetEnvironmentVariable('ELEVENLABS_KEY','User')}
(Invoke-RestMethod -Headers $h "https://api.elevenlabs.io/v2/voices?page_size=100").voices | % { "$($_.voice_id) | $($_.name) | $($_.labels.language) $($_.labels.accent)" }
```

The key lives in the Windows User environment as `ELEVENLABS_KEY`; a shell started before it was
set does not have it, so read the User scope as above. The key needs the `user_read` permission,
because the script reads the account's credit counter before and after voicing; without it the
script stops before spending anything. Premade voices work on every account;
library voices only on accounts that added them.

The model defaults to `eleven_v4`, which covers 90+ languages (Swahili among them). Before voicing
a language, check that it is on that list in ElevenLabs' models documentation.

## 4. Dry run, then voice

```bash
node .claude/skills/voice-book/voiceBook.mjs "<book folder>" --lang en --dry-run
ELEVENLABS_KEY="<key>" node .claude/skills/voice-book/voiceBook.mjs "<book folder>" --lang en --voice <voice id>
```

The dry run lists every text box with the sentences Bloom sees and the character count to be
billed. Read it for text that should not be spoken or that is split oddly before paying for audio.
`--lang` is the Bloom language tag on the `bloom-editable`s (`en`, `swh`, ...).

What gets voiced: the front cover's title, then every text box on the book's own pages, including
canvas text. Other front and back matter and image descriptions are skipped. The title's markup
is also written to its other copies (the data div and the title page): Bloom refills every copy
of a `data-book` field from the data div when it opens a book, so a cover-only change would
vanish. Text boxes that
already have a recording (an audio file named after the box or one of its sentences) are skipped;
`--force` re-records them. Each clip is
`audio/<text box id>.mp3`, with `previous_text`/`next_text` sent so neighbouring pages keep a steady
delivery.

Done when the script prints `Saved <htm path>`, a duration line per text box, and
`ElevenLabs credits used: N`. Report N to the user. It is the account counter's change over the
run, so it also counts anything else that used the account meanwhile.

## 5. Check it in Bloom

Open the collection in Bloom and the book in the Edit tab with the Talking Book tool. A voiced
text box plays with "Check" and has its sentence splits under "Adjust Timings". The tool only
reaches languages the book displays: for a language that is not displayed (English in a
Swahili-only book), tick it in the Edit tab's language menu ("One Language") first.

An agent checking without ears: in the page iframe (named `page`), fetch
`/bloom/api/audio/checkForAnyRecording?ids=<text box id>` for each `.bloom-editable.audio-sentence`;
`true` means Bloom finds the clip. Confirm the markup is still in the `.htm` after Bloom has opened
the book. Then tell the user which pages to listen to; only a person can judge pronunciation.
