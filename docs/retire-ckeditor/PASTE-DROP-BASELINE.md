# Paste / drop baseline — what today's filter lets through

Captured 2026-09-07 by `liveChecks/pasteDropBaseline.mjs` against Bloom on branch `BL-6681-stage1-undostack` (CKEditor 4 with Bloom's `config.js` pasteFilter). This is the behaviour the replacement sanitizer (PLAN.md 4.8) must reproduce; rows are inventory C1–C7.

Each row: the box started as `<p>Start end</p>` with the caret after "Start "; the event carried both `text/html` and `text/plain`.

## C1 — a table

**Input HTML**

```html
<table border="1" style="border-collapse:collapse"><thead><tr><th>Name</th><th>Age</th></tr></thead><tbody><tr><td>Ann</td><td>3</td></tr><tr><td>Bob</td><td>5</td></tr></tbody></table>
```

**After paste** (event was handled (defaultPrevented))

```html
<p>Start NameAge</p><p>Ann3</p><p>Bob5</p><p>end</p>
```

**After drop** (event was handled (defaultPrevented))

```html
<p>StNameAge</p><p>Ann3</p><p>Bob5</p><p>art end</p>
```

## C2 — nested divs with an id (as copied from another Bloom book)

**Input HTML**

```html
<div id="i7a3b2c1" class="bloom-editable bloom-content1" lang="en" style="color:red"><div class="inner"><p>Nested <b>bold</b> text</p><p>Second para</p></div></div>
```

**After paste** (event was handled (defaultPrevented))

```html
<p>Start Nested <strong>bold</strong> text</p><p>Second para</p><p>end</p>
```

**After drop** (event was handled (defaultPrevented))

```html
<p>StNested <strong>bold</strong> text</p><p>Second para</p><p>art end</p>
```

## C3 — iframe, script, style, object, embed

**Input HTML**

```html
<p>Before</p><iframe src="https://example.com/"></iframe><script>window.__pwned=1</script><style>p{color:red}</style><object data="movie.swf"></object><embed src="movie.mp4"><p>After</p>
```

**After paste** (event was handled (defaultPrevented))

```html
<p>Start Before</p><p>After</p><p>end</p>
```

**After drop** (event was handled (defaultPrevented))

```html
<p>StBefore</p><p>After</p><p>art end</p>
```

## C4 — an inline image

**Input HTML**

```html
<p>Picture: <img src="https://example.com/a.png" alt="alt text" width="40" height="40"> end</p>
```

**After paste** (event was handled (defaultPrevented))

```html
<p>Start Picture: end</p><p>end</p>
```

**After drop** (event was handled (defaultPrevented))

```html
<p>StPicture: end</p><p>art end</p>
```

## C5 — styled span soup from a web page

**Input HTML**

```html
<p><span style="font-family:Arial,sans-serif;font-size:14pt;color:#ff0000;font-variant:small-caps;background:yellow;font-weight:bold;letter-spacing:2px">Soup</span> <span class="x" data-foo="1" title="t">plain span</span> <span style="text-decoration:underline">underlined</span></p>
```

**After paste** (event was handled (defaultPrevented))

```html
<p>Start <span style="font-family:Arial,sans-serif;font-size:14pt;color:#ff0000;font-variant:small-caps;background:yellow;font-weight:bold;letter-spacing:2px">Soup</span> <span class="x" data-foo="1" title="t">plain span</span> <span style="text-decoration:underline">underlined</span></p><p>end</p>
```

**After drop** (event was handled (defaultPrevented))

```html
<p>St<span style="font-family:Arial,sans-serif;font-size:14pt;color:#ff0000;font-variant:small-caps;background:yellow;font-weight:bold;letter-spacing:2px">Soup</span> <span class="x" data-foo="1" title="t">plain span</span> <span style="text-decoration:underline">underlined</span></p><p>art end</p>
```

## C6 — a link with extra attributes

**Input HTML**

```html
<p>See <a href="https://example.com/page" target="_blank" rel="noopener" class="lnk" id="l1" title="t" style="color:blue" onclick="alert(1)">this link</a>.</p>
```

**After paste** (event was handled (defaultPrevented))

```html
<p>Start See <a data-cke-saved-href="https://example.com/page" href="https://example.com/page">this link</a>.</p><p>end</p>
```

**After drop** (event was handled (defaultPrevented))

```html
<p>StSee <a data-cke-saved-href="https://example.com/page" href="https://example.com/page">this link</a>.</p><p>art end</p>
```

## C-mixed — a realistic web-page fragment (heading, list, bold/italic, sup)

**Input HTML**

```html
<h2 class="title">Heading</h2><ul><li>One <em>two</em></li><li><strong>Three</strong></li></ul><p>H<sub>2</sub>O and E=mc<sup>2</sup>, <u>under</u>, <s>struck</s>, <code>code</code>.</p>
```

**After paste** (event was handled (defaultPrevented))

```html
<p>Start Heading</p><p>One <em>two</em></p><p><strong>Three</strong></p><p>H2O and E=mc<sup>2</sup>, <u>under</u>, struck, code.</p><p>end</p>
```

**After drop** (event was handled (defaultPrevented))

```html
<p>StHeading</p><p>One <em>two</em></p><p><strong>Three</strong></p><p>H2O and E=mc<sup>2</sup>, <u>under</u>, struck, code.</p><p>art end</p>
```

## Findings

Read the outputs as **live DOM**, not the saved form: `data-cke-saved-href` / `data-cke-saved-src`
are CKEditor bookkeeping that its `getData()` removes on save.

1. **Rows C1, C2, C3, C4, C6 and the mixed fragment behave as the inventory says** — blocks collapse
   to `<p>`s, tables/divs/iframes/scripts/images/headings/lists vanish leaving their text, the div's
   `id` is gone, the link keeps only `href`, `sub`/`s`/`code` are dropped while `em`/`strong`/`sup`/`u`
   survive. **Drop (C7) matches paste in every row**, differing only in where the content lands (the
   drop point, mid-word here, versus the caret).

2. **Row C5 does NOT behave as the inventory says.** The spans arrive with *everything* — `class`,
   `data-*`, `title`, and the whole `style` (font-family, font-size, background, font-weight,
   letter-spacing…), not just `font-variant` and `color`. The filter itself is fine: applied directly,
   `pasteFilter` reduces the same soup to `<span style="color:#ff0000; font-variant:small-caps">` and
   `<span>`. What undoes it is Bloom's own paste handler, one step later
   (`liveChecks/pasteFilterBypass.mjs` logs both):

   `BloomField.restoreHtmlMarkupIfNecessary` (BL-12357) exists to put spans back when the paste came
   from *inside* CKEditor. It decides that by `dataTransfer.getData("cke/id")` — but CKEditor 4.5's
   `dataTransfer` wrapper assigns an id to **every** transfer, external ones included (the logged ids
   are `cke-…`, its generated form). So the test is always true, and whenever the clipboard HTML
   contains `<span style=` the handler replaces the **filtered** `dataValue` with the **full**
   `text/html` from the clipboard.

3. **That bypass is not limited to spans.** With a styled span anywhere in the payload, a table, an
   `<iframe>`, an `<img>` and a `<div id="dup-id">` all reached the box intact; the identical payload
   without the span was filtered to `cell red div text`. Since almost any real web page carries styled
   spans, **the BL-3899 guarantee is effectively off for web-page pastes today.** This is the
   silent-failure case §4.8 warned about, already in production rather than a future risk.

   *Caveat:* captured with synthetic `ClipboardEvent`s. A real Chrome/Word clipboard also carries
   `<!--StartFragment-->` markers, which take the handler's other branch — still the unfiltered
   fragment. Confirm once by pasting a real web-page table with coloured text into a Bloom box.

**Consequences for the plan:** the new sanitizer (Stage 3, `pasteSanitizer.ts`) must implement the
*intended* C5 (`span{font-variant,color}`), not today's behaviour; D8's "detect an internal copy" needs
a test that actually works (`getTransferType() === DATA_TRANSFER_INTERNAL`, or `sourceEditor`); and
this should be fixed on master independently of the project — it is a one-line condition.
