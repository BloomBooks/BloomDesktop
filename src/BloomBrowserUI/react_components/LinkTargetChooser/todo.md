
# Things working in mock but not with the real backend (./manual.sh --backend)
- [ ] after selecting a book, it should be possible to select the url and press delete. This should lead to the deselection of the book and page and the url being empty.
- [ ] after selecting a book, it should be possible to use the paste button to change th url. This should lead to the deselection of the book and page and the url being empty. Cover this is in ui tests.
- [ ] When the front cover is selected, the resulting url should not include the page portion (e.g. /book/bbf2fef8-62df-4ad5-a3f2-8d626b4f8c86 instead of /book/bbf2fef8-62df-4ad5-a3f2-8d626b4f8c86#52ad5abd-f858-4c7e-9157-89279cbb9b3f). We just want to point to the book alone. Make sure tests handle this, both setting it correctly but also parsing incoming urls to set things up properly. E.g. if we see "/book/123" then we want to select book 123 but also its cover page. Cover this is in ui tests.

# Later
Fix CSS in pages (see this with ./manual.sh --backend )
Only fill in contents of pages as they are scrolled into view. Ideally the page numbers would be there immediateley, but not the little thumbnail of the page.
Further optimize for large books: Only create the page buttons themselves as they are needed.

# Low Priority
- [ ] When the URL starts with "\bloom", don't let the user edit that. They can select it and copy it, they can delete the url, they can paste a new url, but they can't edit it.
- [ ] the dialog (or at least the LinkTargetChooser) seems to be (slowly) rerendering on each click of a page. At least it does this in `./manual.sh --backend` mode. See if you can reproduce in a uitest, perhaps using console messages.
