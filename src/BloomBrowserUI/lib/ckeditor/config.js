/**
 * @license Copyright (c) 2003-2015, CKSource - Frederico Knabben. All rights reserved.
 * For licensing, see LICENSE.md or http://ckeditor.com/license
 */

// Warning: this file is NOT processed by yarn watchCode or webpack -w. Takes a full build or manual copy
// (or possibly something like yarn gulp copyFiles) to get changes to take effect during debugging.

// Note: I determined (by searching for 'revision' in ckeditor.js, as some doc suggests) that we are using
// a version built from CkEditor 4.5.1 (c. 2015). The plugins (other than autolink, which I'm not sure
// about but is probably 4.5.1) are taken from the CkEditor repo, using the code in the 4.5.x branch, which
// I believe would be 4.5.11. We seem to be using the 'icy orange' skin. I have not been able to determine
// what other settings we used to build our version.

// In the course of switching to WebView2, I discovered that our icy_orange skin folder contained editor_gecko.css,
// but WebView2 was looking for editor.css, which we didn't have. The lack of it caused our range selection
// toolbar not to appear. I generated a roughly similar configuration using https://ckeditor.com/cke4/builder
// which as of September 2022 produced version 4.19.
// (FWIW: I chose plugins Auto Link, Color Button, Basic Styles, Clipboard, Editor Toolbar, Floating Space,
// Link, and their required dependencies. This was the closest I could get to the features I think we use.
// But the list of subdirectories in the plugins directory of the resulting build was quite different from ours.)
// Next, I pretty-printed the editor_gecko.css files in our build and the 4.19 build; they were very different.
// So, I compared editor_gecko.css with editor.css in the 4.19 build. They differed only in that the gecko version
// added two rules, immediately following a distinctive legend.cke_voice_label rule:
// .cke_bottom{
//     padding-bottom:3px
// }
// .cke_combo_text{
//     margin-bottom:-1px;
//     margin-top:1px
// }
// I found the same sequence of three rules in our editor_gecko.css.
// So, I created an editor.css file for our build by copying the editor_gecko.css and removing those two rules.
// Hopefully, this is identical to the editor.css that was originally part of our 4.5.1 build and got lost.
// At least, having it restores the range selection toolbar.

CKEDITOR.editorConfig = function(config) {
    // Define changes to default configuration here.
    // For complete reference see:
    // http://docs.ckeditor.com/#!/api/CKEDITOR.config

    // The toolbar groups arrangement, optimized for a single toolbar row.
    config.toolbarGroups = [
        { name: "document", groups: ["mode", "document", "doctools"] },
        { name: "clipboard", groups: ["clipboard", "undo"] },
        { name: "editing", groups: ["find", "selection", "spellchecker"] },
        { name: "forms" },
        { name: "basicstyles", groups: ["basicstyles", "cleanup"] },
        {
            name: "paragraph",
            groups: ["list", "indent", "blocks", "align", "bidi"]
        },
        { name: "links" },
        { name: "insert" },
        { name: "styles" },
        { name: "colors" },
        { name: "tools" },
        { name: "others" },
        { name: "about" }
    ];

    // The default plugins included in the basic setup define some buttons that
    // are not needed in a basic editor. They are removed here. Also we don't (yet) want
    // the background color button that the colorButton plugin brings by default.
    config.removeButtons =
        "Cut,Copy,Paste,Undo,Redo,Anchor,Strike,Subscript,About,BGColor";

    // Dialog windows are also simplified.
    config.removeDialogTabs = "link:advanced";

    // this aligns to tool bar with the right side fo the edit field
    config.floatSpacePreferRight = true;

    // This is required to prevent Bloom from crashing when the Undo button is clicked.
    config.undoStackSize = 0;

    // Remove the annoying tooltip "Rich Text Editor, editorN".
    config.title = false;

    // See http://docs.ckeditor.com/#!/guide/dev_acf for a description of this setting.
    config.allowedContent = true;

    // See https://ckeditor.com/docs/ckeditor4/latest/api/CKEDITOR_config.html#cfg-disableNativeSpellChecker
    // This setting results in bloom-editable divs having an attribute 'spellcheck="false"'.
    // No more red squiggles. (BL-12205)
    config.disableNativeSpellChecker = true;

    // Filter out any html that might be dangerous.  Specifically, a div element might be copied from the same book and
    // could introduce duplicate ids.  See https://silbloom.myjetbrains.com/youtrack/issue/BL-3899.
    // Note that the first attempt to fix BL-3899 set allowedContent rather than pasteFilter, but that caused
    // http://issues.bloomlibrary.org/youtrack/issue/BL-3976.
    // The current code (line above and below) should give us what we want in both situations, namely when we change
    // the html via javascript, we can do whatever we want. When the user pastes, he is bounded by the following set.

    // See http://docs.ckeditor.com/#!/api/CKEDITOR.config-cfg-pasteFilter for a description of this setting.
    // BL-4775: Removed span from this, so that you can't paste spans

    // The following is safe:
    //      config.pasteFilter = 'h1 h2 h3 p blockquote table tr th td caption b bdi bdo br em i q strong sub sup u; a[!href]';
    // but by letting people paste things that cannot be duplicated by a user doing a translation, are
    // we leading people to expect formatting in Bloom that translators will not actually be able to replicate?
    // JohnT Oct 8 2021: added 'a[!href]' to allow pasting hyperlinks. Counter-intuitively, the [!href] annotation
    // indicates that an href is required, not that it is forbidden. (Without annotations, listing a tag
    // means it may be pasted, but any attributes in the original will be removed.)
    // Therefore for now we're limiting pasting to things that a translator could also do.
    // The {font-weight} annotation says that a <b> element's font-weight style may be pasted.
    // Code in our paste handler deals with this so it doesn't end up in our documents.
    config.pasteFilter = "p br em i strong sup u; b{font-weight}; a[!href];";

    //BL-3009: don't remove empty spans, since we use <span class="bloom-linebreak"></span> when you press shift-enter.
    //http://stackoverflow.com/a/23983357/723299
    CKEDITOR.dtd.$removeEmpty.span = 0;

    //pasteFromWord works if the plugin is added to plugins, and then these are uncommented,
    //but gives ckeditor "Uncaught TypeError: Cannot read property 'icons' of null".
    //Also, need to add pasteFromWordCleanupFile (of which I could find no example)
    //in order to stop pictures; the on('paste') stops working if you enable this, at least for pastes that come from Word
    //CKEDITOR.config.extraPlugins  = 'pasteFromWord';
    // CKEDITOR.config.pasteFromWordPromptCleanup = true;

    // Add the autolink plugin to make it easy for users to make live internet/email links in ebooks.
    // See https://issues.bloomlibrary.org/youtrack/issue/BL-6845.
    // Add the colorbutton for choosing color of text and background.
    // The others are required dependencies of colorbutton.
    // Note that the BGColor button that comes by default with the colorbutton plugin
    // is removed in the config.removeButtons list above.
    CKEDITOR.config.extraPlugins =
        "autolink,panel,panelbutton,button,floatpanel,colorbutton";
};
