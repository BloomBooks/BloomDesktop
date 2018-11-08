The files in this folder make up the Readium cloud-reader-lite. They are used in the epub preview panel.
The version here was produced from Readium 0.3.2, obtained as follows:
- clone https://github.com/readium/readium-js-viewer.git
- cd to the directory
- set to tag 0.32.0 alpha, (commit 33d123a2044aa65d78f375d88aaab9dcf3a0a5ac).
- git submodule update --init --recursive
- yarn run prepare:yarn:all
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