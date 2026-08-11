// Keeps the user's clicks on books and on the things that act on "the selected book"
// (the Edit/Publish tabs, the Edit button in the collection tab's book pane) in the order
// the user made them.
//
// BL-15971: clicking a book and clicking a tab are two independent API posts. The Bloom server
// hands each to its own worker thread, and those threads race for the server's single API lock;
// whichever wins goes first, regardless of which request arrived first. So a tab click made a
// moment after a book click can reach the lock first and switch tabs while the *previous* book
// is still the selected one - you end up editing the book you clicked away from. Selecting the
// book is also slow (it brings the book up to date before it becomes the current selection), so
// the window is wide enough to hit by hand.
//
// The browser is the only place that knows what order the user clicked in, so we record it here:
// each book selection is chained onto the one before it, and anything that acts on the selected
// book waits for that chain to settle before posting. This makes the book most recently clicked
// the one that gets edited or published.
//
// Note that waiting costs no responsiveness that we didn't already pay: when the requests happen
// to reach the server in the right order, the tab request already blocks on the server's API lock
// for exactly as long.

let bookSelectionChain: Promise<unknown> = Promise.resolve();

/// Register a post that changes which book is selected. It will not be sent until any earlier
/// book selection has finished, and later calls to whenBookSelectionSettled() will wait for it.
/// Returns a promise that resolves when this selection has finished.
export function postBookSelection(
    sendPost: () => Promise<unknown>,
): Promise<unknown> {
    // Both callbacks are the same so that one failed selection doesn't strand the chain; the
    // catch keeps a rejection from propagating to everything queued behind it.
    bookSelectionChain = bookSelectionChain
        .then(sendPost, sendPost)
        .catch(() => undefined);
    return bookSelectionChain;
}

/// Wait until every book selection the user has asked for has actually taken effect in Bloom.
/// Call this before posting anything whose meaning depends on which book is selected.
export function whenBookSelectionSettled(): Promise<unknown> {
    return bookSelectionChain;
}
