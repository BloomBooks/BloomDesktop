The files in this folder make up the R2D2BC web book-reader, based on Readium 2.
They are used in the epub preview panel.

According to comments on Readium 2's website, this is currently the most Developed
web-based reader using their technology; their own efforts have so far focused on
Android, IOS, and Electron. This is as of April 2023; the Readium group claim to be
working actively on a web-hosted version, but I suspect there is really only limited
interest because it is not feasible to support DRM in a web-hosted reader.

The version here was produced from the Bloom branch of the Bloom fork of R2D2BC, obtained as follows
- git clone https://github.com/BloomBooks/R2D2BC.git
- cd R2D2BC
- switch to the Bloom branch
- npm install
- npm run build

This produces a directory dist, containing several files.
The top-level files in this directory were copied to this one (src/BloomBrowserUi/Readium).

Then:
- I copied viewer/index-minimal.html to index.html. (This is an extensively modified sample file which became
the root of our viewer.)
- I moved dist/injectables/style/bloom-readium.css to the root and changed the link to it in index.html.
- I moved glyphicons-halflings-regular.woff to the root and changed the link to it in bloom-readium.css.
(For complex reasons it was easier to make the build process work with these files in odd locations in R2D2BC,
but there is no reason to do so in Bloom.)
- I made a few tweaks moving things in style attributes of index.html to rules in bloom-readium.css

Hints for working on this code:
It's probably easier to work on it in the R2D2BC workspace.
- Put any epubs you want to use as samples in their examples/epubs folder.
- Do npm run build && npm run examples.
- Open a browser on localhost:4444
- Choose the epub you want
- Choose index_minimal.html
If you make changes to index_minimal.html or bloom-readium.css, you will need to tweak them
as described above.
Note the other examples, particularly index_api.html, which illustrates all the kinds of buttons that
R2D2BC supports.
Test any new functionality carefully! As of April 2023, I had to fix quite a few bugs to get media overlays
working right.
