---
name: game-theme-preview
description: 'Audit and fix WCAG color contrast in Bloom game themes. Generates a scrollable HTML preview showing every element of every theme in its real colors with low-contrast pairs flagged, then drives an AI-curated, human-approved fix loop. Use when adding or editing a game theme in gamesThemes.less, when a game element (dashed target, draggable, button, checkbox) is hard to see, or when a tester wants to eyeball all themes at once.'
argument-hint: 'optional: a specific theme name or a color pair to check'
user-invocable: true
---

# Game Theme Preview & Contrast Fix

## Outcome
Every part of every Bloom game theme is verifiable at a glance - each element rendered in
its resolved colors with a WCAG ratio under it - and every low-contrast spot gets an
**AI-curated, palette-aware fix** that the human approves element-by-element in the page
itself. The agent then applies the approved colors and re-verifies. Also: a one-shot CLI
to check the contrast of any two colors.

## When To Use
- You changed colors in `src/content/templates/template books/Games/gamesThemes.less`.
- Someone reports a game element (dashed drop target, draggable, button, checkbox) is
  hard to see against the background.
- A tester wants to scroll through all themes and confirm they look good.
- You need the WCAG contrast ratio between two colors.

## The canonical loop (do it this way)
This is the agreed process. The generator measures, validates, and visualizes; **the
agent chooses the colors** (fitting each theme's palette and name); **the human approves**
each change; the agent applies and re-verifies.

1. **Measure.** Generate the preview from the *source* LESS and publish it:
   ```
   py ".github/skills/game-theme-preview/generate_preview.py" <out.html>
   ```
   (`<out.html>` in the session scratchpad). Publish with the **Artifact** tool; the nav
   chips show each theme's issue count.
2. **Curate (agent judgment, not just the algorithm).** For each flagged element, dump the
   theme's resolved palette and failing checks (call `build()` per theme), then pick a
   color that a designer eyeballing the *whole* theme would choose:
   - reuse a color the theme already uses (text, header, an existing outline),
   - honor the theme's name/identity (keep *coral-reef* coral, *cherry-blossom* pink),
   - prefer one upstream change that fixes several checks (e.g. deepening
     `--game-primary-color` slightly), and note when one variable feeds another,
   - keep intentional translucency (darken / raise alpha rather than going opaque).
   Write the picks to a curated JSON file (scratchpad - it's a per-review input, not a
   repo asset):
   `theme -> "Component title|check label" -> [ {label, why, changes:[[var,value],...]} ]`
3. **Validate + visualize.** Re-run with the curated file as the 3rd argument:
   ```
   py generate_preview.py out.html "<path>/gamesThemes.less" curated.json
   ```
   Each curated pick renders first as a blue **Recommended** option with its rationale, in
   context, and is held to the **same whole-element validation** as a generated one -
   invalid picks are dropped, never shipped. Mechanical alternatives appear below as
   fallbacks. Republish to the same Artifact URL.
4. **Human approves.** The user ticks the options they want and clicks **Copy approved
   changes**; they paste back an apply-ready block.
5. **Apply + re-verify.** Edit the named `.bloom-page.game-theme-*` block(s) in
   `gamesThemes.less` (one variable per line, with a short comment + the issue id).
   Re-run the generator against the edited file and confirm the affected theme's nav chip
   is clean and **no other theme regressed**.

For a single quick check, skip the page entirely: `py contrast.py "#ffde00" "#feefb1"`.

## Tools in this folder
- `contrast.py` - reusable WCAG library. Named colors, 3/6/8-digit hex, alpha compositing
  over a background. CLI: `py contrast.py <fg> <bg>`.
- `generate_preview.py` - parses `gamesThemes.less`, resolves the full `--game-*` cascade
  per theme, measures contrast for every meaningful element pair, generates validated fix
  options, folds in the curated recommendations, and writes a self-contained HTML page.
  Args: `[out.html] [gamesThemes.less] [curated.json]`.

## Background: how theming works
- Themes live in `gamesThemes.less`. Each `.bloom-page.game-theme-NAME` block sets
  `--game-primary-color` / `--game-secondary-color` plus targeted overrides.
- The `.apply-game-theme()` mixin pushes those down into ~25 specific `--game-*`
  variables (text, header, draggable, target outline, buttons, checkboxes, etc.), and
  some variables derive from others (e.g. the selected-checkbox fill follows the
  correct-answer fill) - so one change can fix several elements.
- Elements are styled in `Games.less` (and per-game `.less` files) purely via those
  variables. The dashed **drop target** outline is a CSS border, not an SVG:
  `[data-target-of] { border: dashed ... var(--game-draggable-target-outline-color) }`.
- Key trap: by default `--game-draggable-target-outline-color` follows the draggable
  background, which follows the primary color. If a theme's primary color is close to its
  page background, the dashed target nearly vanishes; give that theme its own
  `--game-draggable-target-outline-color`.

## Contrast thresholds (WCAG 2.1 Level AA)
Bloom targets **Level AA**, so that is what the generator enforces:
- **4.5:1** - normal text (text-on-fill pairs).
- **3.0:1** - large/bold text and non-text UI components / graphics (the dashed target
  outline, draggable-vs-page, button-vs-page, checkbox outline, etc.).
The generator picks the right threshold per pair and flags anything under it. A result
that only just clears its threshold is shown amber, not green, so a barely-passing value
is never dressed up as high contrast. (AAA would require 7:1 / 4.5:1; if that's ever the
goal, raise the thresholds in `build()`.)

## How the generated fallback options are computed
The curated picks come first, but the tool also derives its own options as a backstop, and
they use the same rules (so understand them):
- An element usually has more than one contrast relationship at once (a draggable must
  stand out from the page **and** hold readable text on its fill). Each candidate is
  validated against **all** of that element's checks - a fix can't pass one relationship
  while silently breaking another (no black text on a newly-black fill).
- It picks the right "knob": the foreground, the background fill, or - for a "visible"
  check - the fill *or* the outline. It never recolors the shared page background.
- When a text-bearing element must stand out from a same-tone page, a single-color nudge
  muddies the fill and starves its text, so it also offers a coordinated "darker/lighter
  fill + opposite-tone text" fix.
- Every option lists the **before→after** ratio of each relationship it touches.

## What Good Looks Like
- Every theme's nav chip shows a checkmark (zero low-contrast pairs), or any remaining
  amber/red pair is a deliberate, explained design choice.
- The dashed drop target is clearly visible on every theme's page background.
- Fix colors are drawn from the theme's own palette / identity, not just the nearest
  passing color - and the rationale is recorded (in `why`, and in the LESS comment).
- Applied edits carry a short comment and the issue id; a re-run shows no regressions.

## Notes / Limitations
- The generator reads the **source** `gamesThemes.less`, not the compiled
  `output/.../gamesThemes.css`, so it reflects your edits before a build runs.
- It does not resolve `color-mix()` (only used outside the per-theme blocks today). If a
  theme starts using `color-mix()`, extend `resolve()` in `generate_preview.py`.
- The artifact runs in a sandboxed iframe, so the async clipboard API is often blocked;
  the Copy button falls back to `execCommand` and always shows a selectable text box.
- SVG control-button glyphs are fixed-color image files and are not theme-driven, so they
  aren't measured here.

## Example Prompts
- `/game-theme-preview Regenerate the theme preview so I can check my new theme.`
- `/game-theme-preview Is #ffde00 readable on #feefb1?`
- `/game-theme-preview The drop targets are invisible in garden-path - curate a fix and let me approve it.`
