# branding-report — gotchas (why the code is shaped this way)

Hard-won lessons from building this against the running Bloom. Don't relearn them.

## POST bodies to Bloom must be `Content-Type: text/plain`
`ApiRequest.RequiredPostString()` asserts the content type is text/plain. A bare
`curl --data ...` sends `application/x-www-form-urlencoded`, which trips a `Debug.Assert`
that **terminates Bloom**. `fetch(url,{body:string})` defaults to text/plain (fine), but set it
explicitly. This was the cause of several "Bloom just died" incidents — not any logic bug.

## Switching branding at runtime = subscription override + in-place re-hydrate
The production `POST settings/branding` throws ("branding flows from the subscription code").
The DEBUG handler instead sets `_collectionSettings.Subscription` via
`Subscription.ForUnitTestWithOverrideTierOrDescriptor(Enterprise, descriptor)` and calls
`BringBookUpToDate` on `CurrentSelection`. It's registered `handleOnUiThread:true`, so that book
work runs on the UI thread (safe). It must update the **in-memory selected book** because the
whole-book preview (`book-preview/index.htm` → `CurrentBook.GetPreviewHtmlFileForWholeBook()`)
renders `CurrentSelection` directly — re-selecting the same book path skips the refresh.

## Book.cs update guard needed a `try/finally`
`BringBookUpToDate` set `_doingBookUpdate = true`, ran the update, then set it `false` — with no
`finally`. Any exception mid-update (e.g. the personalization one below) left the flag stuck, so
every later update looked like a concurrent BL-3166 update and (in DEBUG) popped a blocking
MessageBox — wedging the book until restart. Fixed by wrapping the update in `try/finally`. This is
a real latent bug, not just a survey concern.

## Local-Community throws without personalization
Its back-cover template contains `{personalization}`, parsed from the descriptor (the part before
`-LC`). A bare `Local-Community` descriptor yields empty personalization and throws. `survey.mjs`
maps it to `Sample-Community-LC`. Only Local-Community uses `{personalization}` today.

## Capturing several pages from one preview load
The preview holds all pages. Isolating page A hides the others; so before isolating page B you must
**reset all pages' visibility/inline styles**, else B measures 0 width. `isolateExpr` resets first,
and treats 0-width as "page absent".

## Screenshot sizing
Measure the page in a **fixed generous viewport** (set once via `Emulation.setDeviceMetricsOverride`)
— a small/variable viewport makes the preview constrain page width, and pinning the viewport to a
prior page's size shrinks everything. Capture with `clip = {0,0,w,h, scale:2}` + `captureBeyondViewport`
for crisp output that frames exactly the page.

## Screenshotting localhost
The `claude-in-chrome` extension times out injecting into localhost/file pages. Use **headless
Chrome via CDP** (a separate `--remote-debugging-port`) — which is what `survey.mjs` does — and
plain `chrome --headless --screenshot` for one-off page grabs.

## A hot-reloaded static field is null → NRE in MergeBrandingSettings
Symptom: every `setState` returns HTTP **503** with reason phrase (see below) ending in
`NullReferenceException ... at Bloom.Book.BookData.MergeBrandingSettings`. Cause: BL-16370 added
a `static readonly` dictionary `BookStorage.BrandingBadgeHtmlByToken` (expands `{bloom-badge-*}`
tokens). If Bloom was already running when that field was introduced and picked it up via
`dotnet watch` **hot-reload**, the CLR does **not** run the new static field's initializer for the
already-loaded type — so the field is `null`, and `MergeBrandingSettings`'s `foreach (var badge in
BookStorage.BrandingBadgeHtmlByToken)` throws for *every* branding (even an unshipped name, which
falls back to Default's `branding.json`, so the badge loop still runs). Production is unaffected —
a normal start initializes the field. **Fix: fully restart Bloom** (stop `go.sh`, start it again)
so static initializers run; don't rely on hot-reload after adding/changing a static field.

## `request.Failed(text)` puts the message in the status line, not the body
Bloom's `RequestInfo.WriteError(code, text)` writes `text` to the HTTP **StatusDescription**
(reason phrase) and closes the response with an empty body. So to see why a `settings/branding`
POST failed, read `response.statusText`, **not** `await response.text()` (which is empty).

## State restoration
`survey.mjs` restores the original branding always, and layout/xmatter only if it varied them
(baseline layout is read from the preview **before** the loop changes anything; baseline xmatter via
`GET settings/xmatter`, best-effort). The subscription override is never saved to disk, so a Bloom
restart fully restores the real subscription regardless.
