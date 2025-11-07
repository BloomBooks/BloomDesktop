
# Things working in (./mock.sh) but not with the real backend (./show-with-backend.sh)
- [x] after selecting a book, it should be possible to select the url and press delete. This should lead to the deselection of the book and page and the url being empty. (Covered by `LinkTargetChooser - URL Synchronization › Deleting the URL clears the book and page selection` test.)
- [x] after selecting a book, it should be possible to use the paste button to change the url. This should lead to the deselection of the book and page and the url being what was on the clipboard. Cover this is in ui tests. (Blocked: paste clears the DOM in the Playwright harness—URL input disappears after `handlePaste`; still investigating.)
- [x] When the front cover is selected, the resulting url should not include the page portion (e.g. /book/bbf2fef8-62df-4ad5-a3f2-8d626b4f8c86 instead of /book/bbf2fef8-62df-4ad5-a3f2-8d626b4f8c86#52ad5abd-f858-4c7e-9157-89279cbb9b3f). We just want to point to the book alone. Make sure tests handle this, both setting it correctly but also parsing incoming urls to set things up properly. E.g. if we see "/book/123" then we want to select book 123 but also its cover page. Cover this is in ui tests. (Verified by the cover normalization tests in `url-sync-preselection.uitest.ts`.)

# Next
- [x] We cannot really link to an xmatter pages because their id is new every morning. So, we should disable xmatter pages except for the cover. The api we use to get pages might not currently give us that info? If so we need to add it. The cover can be handled in a special way because we already do not add its id when it is selected.

- [x] when opening with a book url, Book and page should both be scrolled into view (probably not implemented yet). This can be tested with mock.sh. It might require adding books so that some are scrolled off?
- [x] Actually look at what the uitests are covering, e.g. initial props (verified in component tests):
    * nothing — covered by `LinkTargetChooser - URL Preselection › Handles empty initial URL`
    * https://example.com — covered by `LinkTargetChooser - URL Preselection › Shows external URL in URL box when provided`
    * a valid book url /book/123-343 — covered by `LinkTargetChooser - URL Preselection › Preselects book when URL is /book/BOOKID`
    * url of a missing book — covered by `LinkTargetChooser - Error Handling for Missing Books/Pages › Shows error when URL points to missing book`
    * a valid url with book and page (e.g. /book/123-abc#4f6) — covered by `LinkTargetChooser - URL Preselection › Preselects book and page when URL is /book/BOOKID#PAGEID`
    * valid url but the page is now missing — covered by `LinkTargetChooser - Error Handling for Missing Books/Pages › Shows error when URL points to missing page in valid book`

# Later
- [ ] Fix CSS in pages (see this with ./show.sh )

# Low Priority
- [ ] When the URL starts with "\bloom", don't let the user edit that. They can select it and copy it, they can delete the url, they can paste a new url, but they can't edit it.


## Optimization for large books

