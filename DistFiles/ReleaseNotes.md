## 1.0.17 - 1 Nov 2013
* Background color on elements wasn't getting through to the PDF

## 1.0.16 - 31 Oct 2013
* Stop complaining if the user has restricted access to the localization files but isn't trying to localize anything

## 1.0.12 - 18 Oct 2013
* enabled pages to incorporate combobox that control layouts dynamically

## 1.0.09 - 14 Oct 2013
* added Spanish localizations
* dropped 'beta' label

## 1.0.08 - 10 Oct 2013
* now supports watermark hints on fields that haven't been filled in yet

## 1.0.06 - 12 Sept 2013
* Windows XP users trying to scan without the necessary windows components should now get a helpful message.

## 1.0.05 - 10 Sept 2013
* Fix style-sheet-related problem when showing Calendar pages

## 1.0.04 - 9 Sept 2013
* New right-click command on the collection to "Replace Missing Images", where you specify a folder to look for replacements.

## 1.0.03 - 6 Sept 2013
* If image file is missing, now shows a message and lets you chose another one.
* New right-click command on the collection to "Check All Books", which finds missing images.

## 0.9.117 29 Aug 2013
* Now prompts for registration starting on 3rd launch.

## 0.9.115 24 Aug 2013
* Added bloom-copyFromOtherLanguageIfNecessary to limit what fields get the copy treatment introduced in 0.9.114

## 0.9.114 24 Aug 2013
* Will now copy some fields from one global language (like English) to the national languages in use if it needs to in order to preserve information in derived books. For example, who the original illustrator was.
* Show "French" as "français" in settings dialog.
* Better error when there's a problem loading a book.
* Fixed forgetting the "experimental commands" setting
* Only show the copyright missing if the copyright itself is missing, not the license or illustrator.
* Don't show a * in the ISBN box
* When making new collection
 * automatically fill in country based on language chosen (will be wrong sometimes, but you see it and can change it).
 * If language is in PNG, auto set National Languages to English and Tok Pisin.
 * Book folders now take their names from vernacular if possible, rather than English. If the vernacular is empty, then the next choice is the National Language 1, then NL2, and only then English, French, Portuguese, or Thai.

## 0.9.111 17 Aug 2013
* New bloom-draggableLabel feature for when images need editable labels

## 0.9.110 17 Aug 2013
* Added missing null xmatter

## 0.9.109 14 Aug 2013
* Update french localizations
* Fix extra blank page problem

## 0.9.105 10 Aug 2013
* B5 pages turned into booklets now properly use B4 paper (were being squished onto B5 paper).

## 0.9.101 27 July 2013
* Allow xmatter to be installed via bloom packs (the creator/distributor of the bloom pack must manually add the xmatter pack to the zip file).

## 0.9.99 23 July 2013
* Collections now sort by "natural sort order" rather than alphabetic, so "Primer 3" comes before "Primer 21", and folio documents will include them in this order, too.

## 0.9.97 23 July 2013
 * Add Folio support, which is a book that sucks in the other books in the collection when it comes time to make a PDF
 * Add AllowNewBooks setting to collection, so that collections can be locked down
 * Add "xmatter" tag on books to select a different front/backmatter pack than the one set as the default for the collection
 * A book can now have a "bookTitleTemplate" so that the title (and thus file and folder name) can be automatically constructed based on fields in the book. E.g. Primer Term {term} Week {week}
 * Slight alteration in page zoom during PDF to address page breaks slowing moving upwards in really large documents.

## 0.9.96 16 July 2013
 * In Publish Tab, new option to turn on "Crop Marks" for commercial printing. In addition to the visible crop marks, there is a matching "TrimBox" for automated cropping.

## 0.9.95 13 July 2013
 * Fixed recent problem where custom font and styles were being ignored.

## 0.9.92 22 June 2013
 * New experimental "Export XML for InDesign" command on the right-click menu of books. Enable via the "Show experimental commands" setting in the Settings dialog.

## 0.9.91 20 June 2013

These changes will only be noticed by those of us writing new templates from scratch in html:

* When starting a book, now removes incoming content with lang = "x", so we can draft with "x" and know that it won't show to users.
* data-hints should now live on the enclosing bloom-translationGroup. On the minus side: for now, they will not dissappear even when there is content.

## 0.9.90 19 June 2013
* Alleviate problem (Windows 7) of sylesheets from old versions sticking around even when you upgrade (VirtualStore).

## 0.9.89 16 June 2013
* Fix page size/layout choices

## 0.9.86 10 June 2013
* Environment variable FEEDBACK set to "off" now disables analytics

## 0.9.83 10 June 2013
* Fix: Stylesheets references are now ordered when saved to disk (were previously ordered within bloom)
* Fix: Front/Back Mater (XMatter) css now saved with the book on disk (for a post-bloom future)
* Improve BloomPack Install responsiveness.

## 0.9.82 7 June 2013
* Fix: If you add an large image and then later crop it, you didn't see the crop on the page until you restarted Bloom.
* Fix: When using the auto-update, 2 copies of the new Bloom would be run.

## 0.9.80 5 June 2013
* When choosing a language, you can now customize how the name is actually displayed. You can also change it later, using the Configuration dialog.

## 0.9.79 4 June 2013
* Localization improvements:
 * Can now localize the column headers of the language chooser.
 * Many more "dynamic" strings available before you chance upon them.

## 0.9.78 4 June 2013
* Language Look Up now searches alternative names from Ethnologue.
* Language Look Up now lists countries
* Books now know the ids of their ancestor templates.
* Experimental InDesign XML Export (write to john if you're interested in helping experiment with this).
* Fixed bug that could cause lost localization strings during upgrade.

## 0.9.71 24 May 2013
* Improved PDF layout when OS is at 120dpi or 144dpi
* Allow BloomPack install even if Bloom is already running
* In the Language chooser, auto-select "Unlisted Language" if user clicks on the related link.
* Pretty-print html in the books

## 0.9.67 24 May 2013
* Updated French Localizations

## 0.9.66 23 May 2013
* Much faster startup time if you have lots of books
* Much better performance when editing book with many pages
* More accurate analytics

## 0.9.55 16 May 2013
* Fixed problem with error on machines lacking Adobe Reader
* Update French Localizations (Thx David Rowe)

## 0.9.54 15 May 2013
 * New "Picture & Word" page in Basic Book for simple one-word-per-page books.
 * A couple localization fixes.
 * Better message if user's machine doesn't know how to follow hyperlinks.

## 0.9.58 10 April 2013
 * New experimental B5 Primer template

## 0.9.53 2 April 2013
* Now Localizable
 * Settings Protection Dialog
 * Language chooser (shows in New Collection Wizard & Settings dialog)
* Now Mostly Localizable
 * Image ToolBox
 * Metadata editor

## 0.947 22 March 2013
* New Collection Wizard is now mostly localizable
* On first run, now detects user's OS Display Language and tries to use that, or offers other choices if it doesn't have that UI language yet.
* Parts of Bloom that come from outside libraries, so not yet localize-able:
 * Language chooser (shows in New Collection Wizard & Settings dialog)
 * Image Toolbox
 * Send/Receive
 * Tooltips in Publish Tab
 * Metadata Dialogs
 * Settings Protection Dialog

## 0.946 21 March 2013
* Add enough space to show French labels, where previously things were cut off.
* Make UI Language Chooser no longer experimental, so it's now on by default.
* Newly localize-able:
 * Page Size/Orientation choices
 * Page thumbnail names (e.g. "Title Page", "Just Text")
 * Open/Create Collection Dialog


## 0.945 20 March 2013
* Reorganized and cleaned up internationalization. Some more work is yet to be done.

## 0.944: 18 March 2013
* Initial French Localization (Thanks David Rowe)

## 0.940: 11 March 2013
* Bloom now checks for the availability of a new version and offers to download it for you

## 0.933: 5 March 2013
* Fix problem of new title not being reflected in the thumbnail until restart
* Improved problem of occasional cursor disappearance after coming in from another application.

## 0.9: 21 December 2012
* Small bug fixes
* No longer times out after 90 days, but it does prompt you to upgrade

## 0.9: 13 October 2012
* Partial Send/Receive support available as an opt-in experimental feature
* Partial Localization support available as an opt-in experimental feature
* Calendar, Picture Dictionary, and Template Maker are now an opt-in experimental feature


## 0.9: Sept 28 2012

* Can now change collection name in the Settings Dialog
* Can now set the default font in the Settings Dialog

## 0.9: Sept 22 2012

* Auto compression of pngs as you select them
* "Update All Books" menu command which updates frontmatter, illustration metadata, and compresses all imags
* Can now paste if there is an image path on the clipboard


## 0.9: Sept 20 2012

* Basic, experimental Send/Receive.

## 0.8: Sept 15 2012

* Pages are auto-zoomed on small screens, so that their full with always shows
* In the publish tab, you can now choose a different page layout (size/orientation)
* In the publish tab, the layout options now include an "A4Landscape Split Across Pages" style option, for making books with large images and text, designed to be read to others

## 0.8: July 16 2012

* To cope with large print-resolution images, Bloom now runs all images through a resolution-shrinking service before displaying them during editing. The full size originals are still used when making the PDF for printing.

## 0.8: July 4 2012

* You can now change images, after a warning, even on books that came from shells. If the image is placeholder (flower), you don't see the warning.
* changed former "missing" label to indicate that the image may also just not have loaded quickly enough
* upgraded to a newer pdfsharp in hopes of helping with a "missing token" error that was reported

## 0.8: June 28 2012

* Bloom is now totally tolerant of missing or messed up Adobe Reader. You just see a message and get reduced functionality.

## 0.8: June 27 2012

* The "image on top" page renamed to "Basic Text and Image"
* The "Basic Text and Image" page, in a5portrait & bi/tri-lingual now places vernacular above the picture

## 0.8: June 26 2012

* Collection folders now sport a "collection.css" file which can be used if necessary to override defaults. This isn't user-friendly by any means, but is a stop-gap measure.
* Similarly, if you place a "book.css" in the folder of the book itself, you can now override styles for just that one book.
* XMatter templates can now have back matter (in addition to the previous front-matter). Just use the class "bloom-backMatter"
* Factor XMatter now makes the back cover, both inside and outside, usable for text.

## 0.8: June 25 2012

* From-scatch overhaul of the First Time and Create Collection experiences. Note that the wizard with different questions depending on whether you choose Vernacular or Source
* Template stylesheets can now specify not just size/orientations they support, but also options which the user can select. The Basic Book now uses this to offer 2 different A4Landscape layouts.

## 0.8: May 14 2012

* Added experimental custom page to Basic Book, with toolbox for adding text and
		images

## 0.8: May 9 2012

* Control-mousewheel now does page zooming
   * Factory FrontMatter now puts the "Credits" page to the inside front cover, replacing the "verso" page
   * Factory FrontMatter now starts logical numbering with the title page as page 1. Doesn't show it, though.
   * New margin model
   * Margins are now sensitive to the odd/even'nes of the page

## 0.8: May 3 2012
* Control-mousewheel now does page zooming

## 0.7: April 13 2012

* BloomPacks.  To distribute collections of ShellBooks, you can now zip the collection, then change it from .zip to .BloomPack.  Take the BloomPack to another user's computer, and double-click it install that pack on their computer.

## 0.6: Feb 10 2012
### Format Changes
These are the last <em>planned</em> format changes before version 1.

* Stylesheets can now support multiple Page Size & Orientations.  A dummy css rule now tells Bloom which size/orientations the sheet supports.  See the format documentation for details.
* Start of a feature for pasting images via the clipboard: hover over the image and click the paste button. Needs some work and testing.




* Introduced "BasicBook" which replaces "A5Portrait Template". "BasicBook" currently supports A5Portrait and A4Landscape.
* Library tab:Double-clicking on a book takes you to the edit mode.
* Library tab: the right-click menu has several new useful commands.
* Edit tab: the toolbar now has a menu for changing the page size and orientation.
* Edit tab, Image Toolbox: several usability improvements.
	* Publish tab: new "Save" button suggests a name which indicates the language and the book portion that was saved to the PDF (e.g. cover, insides, etc.)
* Bubbles only show when the field is empty, or in focus. This makes it easier to see what is left to be done.


## 0.5: Jan 27 2012
### Format Changes

* The div which contains metadata used to be identified with class "-bloom-dataDiv". It is now identified by id, with id='bloomDataDiv'
* The class "imageHolder" is now "bloom-imageContainer"
* All stylesheet classes that used to start with "-bloom" now start with "bloom"
* You can now enter an ISBN #. This number is automatically removed when the book is used as a shell for a new book.
* Although jpegs should be rare in Bloom books (used only for photos), they are now left as jpegs, rather than being converted to PNGs. All other image formats are still converted to PNG.
* User can now make books bilingual or trilingual by ticking selecting one of the national languages.
* A Picture Dictionary template is now available, with resizable and draggable frames. The stylesheet adapts to monolingual, bilingual, or trilingual needs.
* All images contained within a div with class "bloom-imageHolder" are now proportionally resized and centered within the containing div
* Divs with the class "bloom-draggable" can be moved by the user
* Divs with the class "bloom-resizable" can be resized the user
* The standard "A5Portrait" template now uses html div's instead of textarea's, to facilitate future internal formatting commands

### Known Problems

* page thumbnails don't always update.
  * li translations bubble covers "book li in ___".
  * Adding images leaves an unneeded "_orginal" copy.
  * Custom license can't have single quotes (appostrophes)
  * Once a CreativeCommons license is chosen, changing to another option leaves us with the CC license Image.
  * In bi/trilingual books, you can't yet change order of the languages.


## 0.4: Jan 2011
### Known Problems

* <del>Reported installation problems, related to "libtidy.dll"</del>
* Calendar layout should not display new template pages.
* Book cover Thumbnails aren&#39;t updating.
* Spell checking ignores the actual language (will be fixed when we upgrade to
		FireFox 9)
* <del>Source Text bubbles should not be editable</del>
* <del>Book previews should not be editable</del>
* <del>Copyright needs help formatting the year and copyright symbol</del>
* If you change any of the language settings, you must quit and re-run Bloom (or
		stay in Bloom but re-open the project).
* <del>After a long period of inactivity, a javascript error may be reported. This does
		no harm.</del>
* <del>The user interface of publish tab is somewhat at the mercy of you Adobe Acrobat
		installation (which version, how it is configured).</del>
* Many small page layout problems, for example pictures too close to the margin. Final layout issues are easy to fix but not a priority at the moment.


* Copyright/license dialog now has separate "year" field, and auto-generates the "Copyright ©" portion.
* User Settings Dialog:
* Vernacular Language
* National Language
* Regional or secondary national language
* Province
* District
* Front Matter Pack

* Country/Organization-specific Front Matter Packs. See documentation.
* Hint Bubbles for metadata fields
* Source Text in Bubbles with tabs for all the source languages
* Completely re-written format documentation
* Changed how we embed Adobe Acrobat Reader in order to reduce complexity for the non-tech user. Tested with Adobe Reader 10 and Acrobat 7.1
* Topic Chooser
	* Books and images can now have custom prose licenses


## 0.3: Dec 2011
### Format Changes
Style name change: coverBottomBookKind --&gt; coverBottomBookTopic
Shells and templates may no longer include front or back matter pages.

Introduced Factory-XMatter.htm and accompanying stylesheet.&nbsp; The contents
of this are now inserted into each new book.&nbsp; These templates, which will
be replaced by organizations/countries for their own needs, are populated by
data from a combination of sources:


* &nbsp;Data from the library: language name, province, district, etc.
* &nbsp;Data from the &quot;-bloomDataPage&quot; div of the shellbook itself. E.g.,
		acknowledgments, copyright, license.
	<liData from the user. E.g. Translator name.

### &nbsp;UI improvements:

* First editable text box is now automatically focused
* The currently focused text box is now highlighted with a colored border.
  elements with &quot;data-hint=&#39;tell the user something&quot; now create a nice speech-bubble on the right

## 0.3: 9 Dec 2011
Format change: id attributes are no longer used in textareas or img's.
'hideme' class is no longer used to hide elements in languages other than those in the current publication. Instead, hiding is now the reponsibility of "languageDisplay.css", still based on the @lang attributes of elements.
	 evelopers can now right-click on pages and choose "View in System Browser". You can then have access to every bit of info you could want about the html, stylesheets, & scripts, using firefug (Firefox), or Chrome developer tools.

## 0.3: 1 Dec 2011
###Breaking Changes
I've abandoned the attempt to store Bloom files as xml html5.&nbsp; I risked death by a thousand paper cuts.&nbsp; So, as of this version, Bloom uses strictly-valid HTML5 (whether well-formed-xml or not).&nbsp; Internally, it's still doing xml; it uses HTML Tidy to convert to xml as needed.&nbsp; Other validation issues: The header material has changed so that it now passes the w3c validation for html5, with the only remaning complaint being the proprietary &lt;meta&gt; tags. &lt;meta&gt; tags now use @name attributes, instead of @id attributes.&nbsp; See the updated File Format help topic for updated information.

## 0.3: 30 Nov 2011
Making use of a new capabilty offered by html5, many formerly "special" classes have been moved to div-* attributes:

## 0.3: 29 Nov 2011
####Breaking Changes
Moved collections locations to c:\programdata\sil\bloom\collections. Formerly, the sil part was missing. Bloom
now creates this folder automatically. Now avoiding the word &quot;project&quot;, in favor
of &quot;library&quot;. So now you &quot;create a library for your language&quot;. This change also
shows up in configuration files, so if anyone has an existing .BloomLibrary
file, throw away the whole folder.

## 0.3: 25 Nov 2011
A5 Wall Calendar now usable, with vernacular days and months. Could use a graphic designer's love, though.

## 0.3: 24 Nov 2011
In the image toolbox, you can now reuse metadata (license, copyright, illustrator) from the last image you edited.

### Limitations
This version has the following limitations (and probably many others). Feel free to suggest your priorities, especially if you're contributing to the Bloom project in some way :-)

* The font is always Andika (if you have it).
* All books are A5 size.
* You can't control the cover color.
* Diglots are not supported.
* Right-To-Left languages are not supported (but I haven't seen what works and what doesn't)
* If you have a pdf reader other than Adobe Acrobat set up to display PDFs in Firefox, that will also show up in the Publish tab of Bloom, and it might or might not work. PDF-XChange, for exmample, can make the screen quite complicated with toolbars, and doesn't auto-shrink to fit in the page.
* You can't tweak the picture size, the text location, etc.
