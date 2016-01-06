/**
 * @license Copyright (c) 2003-2015, CKSource - Frederico Knabben. All rights reserved.
 * For licensing, see LICENSE.md or http://ckeditor.com/license
 */

CKEDITOR.editorConfig = function( config ) {
	// Define changes to default configuration here.
	// For complete reference see:
	// http://docs.ckeditor.com/#!/api/CKEDITOR.config

	// The toolbar groups arrangement, optimized for a single toolbar row.
	config.toolbarGroups = [
		{ name: 'document',	   groups: [ 'mode', 'document', 'doctools' ] },
		{ name: 'clipboard',   groups: [ 'clipboard', 'undo' ] },
		{ name: 'editing',     groups: [ 'find', 'selection', 'spellchecker' ] },
		{ name: 'forms' },
		{ name: 'basicstyles', groups: [ 'basicstyles', 'cleanup' ] },
		{ name: 'paragraph',   groups: [ 'list', 'indent', 'blocks', 'align', 'bidi' ] },
		{ name: 'links' },
		{ name: 'insert' },
		{ name: 'styles' },
		{ name: 'colors' },
		{ name: 'tools' },
		{ name: 'others' },
		{ name: 'about' }
	];

	// The default plugins included in the basic setup define some buttons that
	// are not needed in a basic editor. They are removed here.
	config.removeButtons = 'Cut,Copy,Paste,Undo,Redo,Anchor,Strike,Subscript,Superscript,About';

	// Dialog windows are also simplified.
	config.removeDialogTabs = 'link:advanced';

	// this aligns to tool bar with the right side fo the edit field
	config.floatSpacePreferRight = true;

	// This is required to prevent Bloom from crashing when the Undo button is clicked.
	config.undoStackSize = 0;

	// Remove the annoying tooltip "Rich Text Editor, editorN".
	config.title = false;

    // Don't filter out any html that couldn't be produced by the toolbar
	config.allowedContent = true;

    //BL-3009: don't remove empty spans, since we use <span class="bloom-linebreak"></span> when you press shift-enter.
    //http://stackoverflow.com/a/23983357/723299
    CKEDITOR.dtd.$removeEmpty.span = 0;

    //pasteFromWord works if the plugin is added to plugins, and then these are uncommented,
    //but gives ckeditor "Uncaught TypeError: Cannot read property 'icons' of null".
    //Also, need to add pasteFromWordCleanupFile (of which I could find no example)
    //in order to stop pictures; the on('paste') stops working if you enable this, at least for pastes that come from Word
    //CKEDITOR.config.extraPlugins  = 'pasteFromWord';
    // CKEDITOR.config.pasteFromWordPromptCleanup = true;
};
