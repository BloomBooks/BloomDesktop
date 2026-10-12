# Sample Shells

We ship these books already updated, so users never wait for Bloom to update them.
`ShippedSampleShellsTests` fails when they fall behind. To re-update them:

1. Copy this whole folder somewhere outside the repo, and open the copy as a collection in Bloom.
   `SampleShells.BloomCollection` is English/English with no sign language and Traditional
   front/back matter. Keep it that way, or Bloom adds that collection's languages to the books.
2. Run **Update Book** on each book.
3. Copy each book's `.htm`, `meta.json`, `appearance.json` and `branding.css` back here, plus any
   image the book now references. (Bloom serves an empty `branding.css` when previewing a shell
   that lacks one, and the back-cover QR code then shows at full size.) Leave out the other
   support files Bloom writes into the book folder (other CSS, `.bak`, `publish-settings.json`).
4. Run prettier on the copied `.htm` and `.json` files (the pre-commit hook does this), so the
   diff shows real changes rather than formatting.
