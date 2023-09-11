# SymChar

SymChar (Symbols for Characters) is an open font that can be used by applications to show symbols that represent 'invisible' characters (such as NBSP). There are also symbols for many common keyboard keys and a few social media sources. The symbols are encoded in the Unicode PUA (Private Use Area) to avoid problems with the display of the characters they represent. The design of the 'invisible' symbols generally reflects the style used by Adobe InDesign and Affinity Publisher but is not intended to be identical.

There are two variants, released in separate packages:

- SymChar - The package for general use, with all symbols in the PUA
- SymCharK - A version that adds some basic Latin letters and symbols, specifically for use by Keyman. This incorporates all the symbols from the KeymanWeb-OSK font. 

Each package includes TTF, WOFF, and WOFF2 versions (see the web folder).

These fonts are intended mainly for use by SIL and partner applications. You are welcome to use the fonts outside of those projects, but be aware that the fonts may change without notice to best serve those applications.

## Encoding details

For a full list of all the symbols available see [Encoded Symbols](documentation/encoding.md).

## Font development processes

The font sources are in UFO3 format. The build process requires [smith](https://github.com/silnrsi/smith) and project build parameters are set in the [wscript](wscript).

Changes are made only to the SymCharSource-Regular.ufo source. The `preflight` scripts regenerate the SymChar and SymCharK UFOs used for the build process. 

## General information

For copyright and licensing - including any Reserved Font Names - see [OFL.txt](OFL.txt).

For practical information about modifying and redistributing this font see [OFL-FAQ.txt](OFL-FAQ.txt).

For more details about this project, including changelog and acknowledgements see [FONTLOG.txt](FONTLOG.txt).

If you want to contribute to the project let us know.
