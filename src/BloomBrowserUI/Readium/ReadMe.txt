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
the root of our viewer. I don't want to rename it in the R2D2BC repo because it may be helpful to be able to
see what I changed. On the other hand, index-minimal.html would be a confusing name for the root file in
our own repo's Readium viewer.)
- I moved dist/injectables/style/bloom-readium.css to the root and changed the link to it in index.html.
(I had a hard time getting it copied to the root dist directory so it would work like this in R2D2BC,
and just putting it there by hand doesn't work because the build starts by deleting dist. Putting it
in injectibles was a kludge, especially since I could not get it to actually work as an injectable; I must
not have figured out that mechanism. But I don't see any reason to leave it in an awkward location in our repo.)
- I moved glyphicons-halflings-regular.woff to the root and changed the link to it in bloom-readium.css.
(Again it was more work than I thought worthwhile to get it copied to dist so it would just work like this in R2D2BC,
especially since the real R2D2BC probably doesn't want anything to do with glyphicons-halflings. It was just
easier to keep the way we were already doing these buttons for Readium 1.)
- I made a few tweaks moving things in style attributes of index.html to rules in bloom-readium.css
(Possibly it would be better to do this in R2D2BC so there's less to tweak if we make further changes there.
I think I had a vague thought of changing as little as possible, but most of this stuff is new.)
- I copied index.html to indexRtl.html, and swapped the calls to previousPage and nextPage so the buttons will
go the right way. (I also added class rtl to the body, though nothing uses this yet. The hope was that we could
detect being on the front or back cover page, and disable the appropriate button using the rtl class. But I can
find no easy way to detect what page we're on. Even something like body:has(.outsideFrontCover) won't work
because the pages are inside another level of iframe from the buttons.)
(This is a kludge. I'm sure there is a more elegant way to automatically generate a different index.html
file. Or find another way to make the buttons do the opposite thing for RTL. Or pull from R2CdBC (there's a commit
d799f2627a88435e3599e783ae178d023736686e on 12/13/2023 that adds some degree of RTL support, including for
swiping...but I don't think it would fix our buttons).
But unless we're somewhat regularly generating new versions of this, I just don't think it's worth it.)

It might be better to make an npm module of our fork of R2D2BC and import it. I am reluctant to do this for various reasons.
- I don't really expect our fork of R2D2BC to be useful to anyone else or any of our other projects,
so I'm reluctant to clutter up npm.
- The problems above would have to be solved to get the dist directory produced exactly how Bloom wants it.
- In particular, the index-minimal.html file that I'm using as the root of Bloom's previewer is not
intended to be a shippable file that gets put in dist.
- It would also take extra work on the Bloom side to switch to getting Readium from npm. What I did just
replaces the old Readium and was easier.

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
