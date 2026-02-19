# Branding + Flavor Notes

## What this folder is
- Each subfolder under `src/content/branding/` is a branding project (for example `Pioneer-Bible`, `Default`, `GSLT-MLDE`).
- Typical branding folder contents:
  - `branding.json`: presets that populate `data-book` fields in xMatter/book DOM.
  - `branding.less` (compiled to `branding.css`): visual/layout rules for branding content.
  - image assets (`.svg`, `.png`, `.jpg`).
  - optional `summary.htm`: text/logo shown in subscription settings summary.

## How subscription descriptors map to branding
Descriptor parsing happens in `src/BloomExe/web/controllers/BrandingSettings.cs` (`ParseSubscriptionDescriptor`).

Descriptor shape (before numeric date/checksum suffix):
- `ProjectName`
- `ProjectName[Flavor]`
- `ProjectName(SubUnit)`
- `ProjectName(SubUnit)[Flavor]`

Special suffixes:
- `-LC` -> `Local-Community` branding
- `-Pro` / `-Trainer` -> `Default` branding

Flavor behavior:
- If flavor exists, Bloom first looks for `branding[Flavor].json`.
- If not found, Bloom falls back to `branding.json` and replaces `{flavor}` tokens in preset content.
- `summary.htm` also receives `{flavor}` and `SUBUNIT` replacement.

## Warning
If you see something like `"content": "<img class='branding vanilla' src='/bloom/branding/Default/made-with-bloom-badge.svg'/>",` this is a dangerous error. It will work correctly in the Bloom editor, where the server will look up that path. But then when this html is used elsewhere, that path will not exist. So currently, all images need to be copied into the branding folder and then referenced without a path, as in `  "content": "<img class='branding vanilla' src='made-with-bloom-badge.svg'/>".

## Build pipeline for branding assets
From `src/content/package.json`:
- `build:branding:less` compiles `branding/**/*.less` to `output/browser/branding/**/branding.css`.
- `build:branding:files` copies branding assets (`png/jpg/svg/css/json/htm`) to `output/browser/branding`.

Useful commands (run in `src/content`):
- `yarn build:branding:less`
- `yarn build:branding:files`
- `yarn build` (full content build)
