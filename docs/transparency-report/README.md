# Transparency comparison report (BL-16778)

Bloom makes the white background of line-art pictures transparent so that a cover or page colour
shows through. Choosing how to do that from one picture on one cover colour has misled us before:
a method that looks right on pale blue can tint grey shading pink on green. This report runs a set
of test pictures through each method, using Bloom's real C# code, and shows every result over many
background colours from light to dark (CIE L* 95 down to 25, three hues at each step), plus white,
the cover colour from the bug report, and Bloom's own cover colour swatches. The browser draws each
result over its colour, as Bloom's pages do.

## Running it

From the repository root:

```bash
node docs/transparency-report/fetch-images.mjs
build/agent-dotnet.sh test src/BloomTests/BloomTests.csproj --filter "TestCategory=TransparencyReport"
node docs/transparency-report/make-report.mjs
```

The first command downloads the test pictures listed in `docs/transparency-report/images.json`
from Bloom Library into `output/transparency-report/images/` and writes
`output/transparency-report/images.json`. It only needs running again when that list changes.

The second command runs the explicit test `TransparencyReportTests`, which the normal test run
skips. It reads `output/transparency-report/images.json` and the pictures in
`output/transparency-report/images/`, and writes:

- `out/<method>/<id>.png`: the full result, as Bloom would produce it;
- `out/<method>/<id>.preview.png` and `out/original/<id>.preview.png`: copies at most 480 pixels wide;
- `results.json`: for each picture, whether Bloom's line-art check
  (`ImageUtils.ShouldMakeBackgroundTransparent`) would make it transparent at all, its size, and for
  each method the time taken and the share of pixels that end up fully see-through, partly
  see-through and solid.

The third command builds `output/transparency-report/report/`: `index.html` plus the images it
uses, with relative paths, so the folder can be opened from disk or published as it is. It refuses
to write more than 60 MB of images; the large view uses the full-size results only when they fit.

The options at the top of `make-report.mjs` make a smaller page to share. The copy linked on
BL-16778 was built with:

```bash
node docs/transparency-report/make-report.mjs --colours "#51C7E9,white,#B0DEE4,#FEBF00" \
  --methods-shown "6.5 beta initial,6.5 Final" --artifact --page-dir <folder>
```

`--artifact` leaves out `<html>`, `<head>` and `<body>`, which a claude.ai artifact supplies.

## Adding a picture

Add an entry to `docs/transparency-report/images.json`:

```json
{ "id": "26-ink-abc123", "file": "26-ink-abc123.png", "kind": "ink",
  "source": "https://bloomlibrary.org/book/abc123", "originalName": "the picture's file name in that book.png",
  "note": "What this picture is here to show." }
```

`kind` is `ink`, `gray-shaded` or `synthetic`; the report's filter lists whatever kinds it finds. `fetch-images.mjs`
downloads the file named `originalName` from the files of the Bloom Library book in `source`; when
`source` is not a Bloom Library book, it copies `source` as a path relative to the repository root. Then run all three commands again. Do not edit
`output/transparency-report/images.json` by hand; the fetch script overwrites it.

## Adding a method

The methods live in `src/BloomTests/ImageProcessing/TransparencyAlgorithms.cs`. Write a function
that takes the 32bpp ARGB bitmap of the whole picture and returns a new 32bpp ARGB bitmap with the
alpha you want, and add one entry to the `All` list:

```csharp
new TransparencyAlgorithm
{
    Id = "my-idea",
    Description = "One plain sentence saying what it does.",
    Run = img => LikeProduction(img, MyIdea),
},
```

`LikeProduction` wraps the function the way Bloom does: it leaves pictures that already have
see-through pixels alone, handles palette pictures by making their near-white palette colours
see-through, and shrinks the result to at most 1000 pixels wide. So the methods differ only in what
they do to each pixel. The report reads the method list and descriptions from `results.json`, so the
page needs no change.

## The methods compared

- `6.5 Final` calls Bloom's `RuntimeImageProcessor.MakePngBackgroundTransparent` itself, so it is what this code does now.
- `Before 6.5`, `6.5 beta initial`, `ink-over-white`, `deficit-keep-rgb` and `ramp-force-grey` are
  earlier versions copied from git; each function's comment names its commit.
- `narrow-ramp` and `edge-fill` were proposed on BL-16778 and are in no commit.

## The synthetic pictures

The pictures whose kind is `synthetic` were drawn to make the search for a scan's paper go wrong:
a gray sky larger than the white paper, a drawing that is mostly mid gray, unevenly lit and cream
scans, pencil only a little darker than the paper, a coloured scene with no white but a few light
highlights, and a scan saved as a palette PNG. `make-synthetic.py` draws them into
`docs/transparency-report/synthetic/`; it needs Python with numpy and Pillow, and only needs running
again to change a picture.

The page remembers, in the viewer's browser, which pictures, methods, kinds and colours were
hidden, so regenerating or republishing it keeps them hidden.
