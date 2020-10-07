The files in this folder make up the Readium cloud-reader-lite. They are used in the epub preview panel.
The version here was produced from Readium 0.3.2, obtained as follows:
- git clone --recursive -b master https://github.com/readium/readium-js-viewer.git readium-js-viewer
- cd readium-js-viewer
- git submodule update --init --recursive
- npm run prepare:all
- git checkout develop && git submodule foreach --recursive "git checkout develop"
Add two patches: in readium-js/readium-shared-js/views/cfi_navigation_logic.js, at the start of the function getLeafNodeElements, Add
			if ($root.length === 0) {
				return [];
			}
(This prevents a crash when testing the visibility of an element with display:none and no siblings)

In the function checkVisibilityByRectangles, after the line
    var clientRectangles = getNormalizedRectangles($element,visibleContentOffsets);
insert
    looper = $element;
      while (looper.length && clientRectangles.length === 0) {
        looper = looper.parent();
        if (looper.prop("tagName") === "BODY") {
          break;
        }
        clientRectangles = getNormalizedRectangles(looper,visibleContentOffsets);
      }
(This allows an element to be considered visible, for purposes of choosing the first element to play audio for,
if it is display:none but one of its parents is visible. This is often true of our image descriptions. It's
possible that it would be better to more carefully restrict this change to the one context where we want it,
but all seems to be well with this simple change and the more restrictive one would involve changing many
more places. I don't think it's worth it unless we decide to fully fork readium and all its submodules. All
These changes are just helping us hold on until Readium 2 is stable.)
(I would like to check this in, but am having trouble with the submodules. When I work my way to this point
GitKraken tells me the submodule is in a detached head state.)
- npm run dist

This produces a directory dist, containing one called cloud-reader-lite.
The contents of this directory were copied to this one (src/BloomBrowserUi/Readium).
There should be nothing in this folder except
- the files copied from cloud-reader-lite
- this ReadMe.txt
- bloomReaderTweaks.css

I deleted many of the cloud-reader-lite files to save space in our installer
- scripts/mathjax/*, guessing that it has to do with embedding equations in books, which we don't need.
- scripts/zip/*, pretty sure this is for handling compressed epub files, and our preview presents the book unzipped
- the three font folders in font-faces (presumed not needed, since we're hiding the font face control)
- css/annotations.css, since we're not supporting annotations in epub previews
- many items from images, that are for controls we're hiding. For now I kept the ones for larger and smaller text,
 since we might reinstate that control. Not sure whether the margin ones are needed.
- all the variants in the fonts folder except glyphicons-halflings-regular.woff. Seems we need one of these
 for the icons used for audio play control, and this one (small) file was enough.

The following line should be inserted in Readium's index.html after where Readium's own
stylesheet is loaded:

    <link rel="stylesheet" type="text/css" href="bloomReadiumTweaks.css">

See the comments in that file for what we tweak and why.

I think I read that it is possible to set some kind of flag to produce a non-minified version,
but don't remember the details.

Hints for working on this code:
If you are only changing javascript, it is easier to just copy scripts/readium-js-viewer_all_LITE.js to Readium/scripts.
If you want to debug with source code, also copy cloud-reader-lite_sourcemap/readium-js-viewer_all_LITE.js.map
to the same place as the js. Don't check this in.
If you want to debug one of our own books using the Readium repo debug setup (npm run html, or npm run http:watch),
- unzip the book
- add the book folder to Readium's epub_content folder
- edit epub_library.opds and create another entry for your book. I often copy an existing one
and just change the title and, critically, the href of the application/epub link, which must
match the name of your folder.
