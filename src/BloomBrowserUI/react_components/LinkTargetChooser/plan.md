LinkTargetChooser is a react component that lets you choose book or a page in a book as the target of a hyperlink.
Props:
* currentURL --> this sets up the current state, selecting books, pages, or the URLEditor if its not a bloom link ( // bloom links look like  #PAGEID or /book/BOOKID or /book/BOOKID#PAGEID. See /src/BloomBrowserUI/bookEdit/bookLinkSetup/BookLinkTypes.ts)
* optional callback to receive the chosen URL, the thumbnail of the chosen book (can be null), the title of the chosen book (can be null). This fires on OK.

It uses bloom/api calls to get thumbnails and folderNames of all the books in the collection, including the current book (see LinkGridSetup.tsx).

It shows these on the left. When you choose one, you can click "OK" in the dialog and a callback (from a prop) should be called with the current url for the book (see LinkGrid for making these urls).

But if you don't click OK, you can instead choose from the pages of the selected book in the PageList on the right. Then if you click OK, the dialog closes and sends back the url to the book and page.

As a third option, there is a space at the bottom of the LinkTargetChooser where you can paste an arbitrary URL. This should have a small paste icon next to it.

Use the materialui components, { css } from "@emotion/react", and bloomMaterialUITheme.

Don't worry about accessibility.

TODO:
- [x] create a URLEditor component that lets you paste in a raw URL. It takes an existing URL as a prop and has a callback for setting the URL. It has a button for pasting, an a check button that causes your default browser (not this app) to open new tab to that link.


- [x] extract out a react component, "BookList" that shows all the books and lets you select them. Props include whether to include the current book, and an optional list of books to show as disabled (see existing styling) (because they have already been used in the LinkGrid). At most one book can be selected at a time.

- [x] create a react component named "PageList" that can show all the pages of a book in a similar chooser, with the page thumbnail on top and the page number  below. See pageThumbnailList.tsx which is less generic but will give you the idea. There is some question as to whether the api supports getting the necessary info from a book that is not the "current" book of the app. If so, just show the pages of the current book, for now. When a book is selected in the BookList that is not the current book, do not populate the page list but just show one choice labeled "Cover Page".

- [x] create the LinkTargetChooser react component that has the BookList on the left, the PageList on the right, and

- [x] a wrapper BloomDialog component, "LinkTargetChooserDialog", that contains the LinkTargetChooser. Use the standard BloomDialog components (there are many examples) to get the title, middle, accept and cancel pieces. This has a callback that gives the chosen link info if OK was chosen, or undefined if the user doesn't want a link anymore (ui doesn't support that yet). If the user clicks cancel then the callback is not called. OK is enabled only when we have a link.

- [x] set up ui testing according to prompts/bloom-uitest.prompt.md. Intercept apis as needed. Verify that everything works.


URL mutual exclusivity - If user selects a book/page AND pastes a URL, whichever they've done most recently takes precedence. SO if I past a URL but then choose a book, the URL disappears. If I choose a book but then paste a URL, the book become unselected.

Thumbnail/title in callback - These can be null, when the link is coming from the URLEditor.

All componetns should be in separate files. The component we will be sharing with the LinkGridSetup can live in this folder.
