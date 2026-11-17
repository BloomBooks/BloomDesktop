# Uploading the old xmatter description translations to Crowdin

The descriptions of the front/back matter packs used to live in `description-<lang>.txt` files
in each pack's folder under `src/content/templates/xMatter`. They are now XLF strings with ids
like `CollectionSettingsDialog.BookMakingTab.Front/BackMatterPack.Paper Saver.Description`
(standard packs in `Bloom.xlf`, project-specific packs in `BloomLowPriority.xlf`).

Only three packs had translations: Paper Saver (`Factory-XMatter`), Super Paper Saver and
Traditional. `translations.json` holds them, taken from the deleted files with the `[V1]`
version prefix and any byte-order mark removed. All of them were at the same version as the
English, so all were current. (Traditional's Bengali file had no `[V1]` prefix, so Bloom never
actually showed it; it is included anyway.)

Traditional's English was reworded in the same change, from "\* Credits on the back of the title
page." to "Like a printed book, this puts the credits on the back side of the title page." Its
translations are of the old text (with the stray "\* " removed), so they go up as suggestions
for a proofreader to accept or improve.

## When

Only after the XLF change has merged to **master** and Crowdin has synced it, which takes a
few minutes. Before that, Crowdin does not have the ids, and the script stops and lists them.

## How

Follow the `crowdin-api` team skill: writes must go in as **SILCrowdinBot**. Ask which User
environment variable holds the bot's token on your machine, then in one PowerShell call:

```powershell
$env:BOT_TOKEN = [System.Environment]::GetEnvironmentVariable('<bot token variable>','User')
node docs/xmatter-descriptions-to-crowdin/upload.mjs            # dry run
node docs/xmatter-descriptions-to-crowdin/upload.mjs --upload   # real upload
```

The script checks that the token is the bot's, uploads one small XLF per language containing
only these units, and leaves them as unapproved suggestions (`autoApproveImported: false`),
for each language's proofreader to approve.

Afterwards, spot-check a language in the editor, e.g.
`https://crowdin.com/editor/sil-bloom/34/en-fr#q=BackMatterPack`: each description should show
a suggestion by SILCrowdinBot, with nothing approved.

Delete this folder once the upload is done.
