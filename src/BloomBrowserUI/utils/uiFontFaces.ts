import uiFontFacesCss from "../bloomUIFontFaces.css?raw";

// Makes sure the current document has the @font-face declarations for Bloom's UI fonts
// (Roboto, NotoSans, SymChar, and the Andika fallback of BL-14102), installing them if --
// and only if -- they are not already present.
//
// Why the guard matters (BL-15300): if a duplicate @font-face for a family that is already
// rendered gets added to a document, CSS font matching switches the rendered text to the new,
// not-yet-loaded copy of the font, and the text goes blank for several frames while it
// re-loads. The edit page, toolbox, and reader-setup documents get these declarations from a
// stylesheet linked in their <head> (see bloomUIFontFaces.css), so for them this must be a
// no-op; for the various WinForms-hosted React documents, which have no such static link,
// this installs the fonts before the first React render commits.
//
// The document.fonts check is reliable at the time we are called because pending stylesheet
// <link>s block script execution, so any statically-linked declarations are already in the
// font set before any of our code runs.
export const ensureUiFontFacesInstalled = (doc: Document = document): void => {
    if (doc.getElementById(kUiFontFacesStyleId)) return;
    // doc.fonts is undefined in jsdom (unit tests); there, treat the faces as not declared
    // and fall through to injecting, which is harmless.
    const alreadyDeclared =
        doc.fonts &&
        Array.from(doc.fonts).some(
            (face) => face.family.replace(/["']/g, "") === "Roboto",
        );
    if (alreadyDeclared) return;
    const style = doc.createElement("style");
    style.id = kUiFontFacesStyleId;
    style.textContent = uiFontFacesCss;
    doc.head.appendChild(style);
};

const kUiFontFacesStyleId = "bloom-ui-font-faces";
